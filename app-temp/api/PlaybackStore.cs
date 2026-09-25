using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Microsoft.Extensions.Primitives;

public sealed record SessionView(string Id, string Title, string NoteMarkdown, int NoteVersion, long NoteProcessedThroughMs, List<Material> Materials, List<Transcript> Transcripts, List<ChunkRecord> Chunks);
public sealed class SessionRecord { [BsonId] public string Id { get; set; } = ""; public string Title { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public sealed class Material { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string Name { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class Note { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public int Version { get; set; } public string Markdown { get; set; } = ""; public string Author { get; set; } = ""; public long ProcessedThroughMs { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class Transcript { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string SourceId { get; set; } = ""; public long StartMs { get; set; } public long EndMs { get; set; } public string Original { get; set; } = ""; public string? Translation { get; set; } public string? Revision { get; set; } public bool Uncertain { get; set; } }
public sealed class CitationRecord { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string QuestionId { get; set; } = ""; public string Kind { get; set; } = "web"; public string Url { get; set; } = ""; public string Title { get; set; } = ""; public int StartIndex { get; set; } public int EndIndex { get; set; } public bool Private { get; set; } = true; }
public sealed class ChunkRecord { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string SourceId { get; set; } = ""; public long Sequence { get; set; } public long StartMs { get; set; } public long EndMs { get; set; } public string Hash { get; set; } = ""; public string Status { get; set; } = "pending-asr"; public string? Error { get; set; } [BsonIgnore] public string Path { get; set; } = ""; }

public sealed class PlaybackStore
{
    readonly IMongoDatabase db;
    readonly string audioRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "audio"));
    public PlaybackStore()
    {
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI") ?? "mongodb://127.0.0.1:27017");
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        db = new MongoClient(settings).GetDatabase("playback_prototype");
    }
    IMongoCollection<T> Collection<T>(string name) => db.GetCollection<T>(name);
    public async Task<bool> IsReady() { try { await db.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1)); return true; } catch (Exception ex) when (ex is MongoException or TimeoutException) { return false; } }
    public async Task<object> CreateSession(string title)
    {
        if (title.Length is < 1 or > 120) throw new InvalidOperationException("Title must be 1–120 characters");
        var item = new SessionRecord { Id = Guid.NewGuid().ToString("N"), Title = title, CreatedAt = DateTime.UtcNow };
        await Collection<SessionRecord>("sessions").InsertOneAsync(item);
        return new { item.Id, item.Title };
    }
    public async Task<List<SessionRecord>> Sessions() => await Collection<SessionRecord>("sessions").Find(FilterDefinition<SessionRecord>.Empty).SortByDescending(x => x.CreatedAt).Limit(20).ToListAsync();
    public async Task<SessionView?> Session(string id)
    {
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (session is null) return null;
        var materials = await Collection<Material>("materials").Find(x => x.SessionId == id).ToListAsync();
        var transcripts = await Collection<Transcript>("transcripts").Find(x => x.SessionId == id).SortBy(x => x.StartMs).ToListAsync();
        var chunks = await Collection<ChunkRecord>("chunks").Find(x => x.SessionId == id).SortBy(x => x.StartMs).ToListAsync();
        var note = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
        return new(session.Id, session.Title, note?.Markdown ?? "", note?.Version ?? 0, note?.ProcessedThroughMs ?? 0, materials, transcripts, chunks);
    }
    public async Task<object> AddMaterial(string id, MaterialInput input)
    {
        if (input.Name.Length is < 1 or > 120 || input.Text.Length is < 1 or > 250_000) throw new InvalidOperationException("Material size or name is invalid");
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == id) == 0) throw new InvalidOperationException("Session not found");
        var material = new Material { Id = Guid.NewGuid().ToString("N"), SessionId = id, Name = input.Name, Text = input.Text };
        await Collection<Material>("materials").InsertOneAsync(material);
        return new { material.Id, material.Name };
    }
    public async Task<object> SaveNote(string id, string markdown, string author, long processedThroughMs = 0)
    {
        if (markdown.Length > 250_000) throw new InvalidOperationException("Note is too large");
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == id) == 0) throw new InvalidOperationException("Session not found");
        var previous = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
        var note = new Note { Id = Guid.NewGuid().ToString("N"), SessionId = id, Version = (previous?.Version ?? 0) + 1, Markdown = markdown, Author = author, ProcessedThroughMs = Math.Max(processedThroughMs, previous?.ProcessedThroughMs ?? 0), CreatedAt = DateTime.UtcNow };
        await Collection<Note>("notes").InsertOneAsync(note);
        return new { note.Version, note.Markdown, note.Author };
    }
    public async Task<List<Note>> NoteHistory(string id) => await Collection<Note>("notes").Find(x => x.SessionId == id).SortBy(x => x.Version).Limit(100).ToListAsync();
    public async Task<ChunkRecord> SaveLocalChunk(string sessionId, string sourceId, long sequence, long startMs, long endMs, string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        stream.Position = 0;
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["sessionId"] = sessionId, ["sourceId"] = sourceId, ["sequence"] = sequence.ToString(CultureInfo.InvariantCulture),
            ["startMs"] = startMs.ToString(CultureInfo.InvariantCulture), ["endMs"] = endMs.ToString(CultureInfo.InvariantCulture), ["sha256"] = hash
        });
        var file = new FormFile(stream, 0, stream.Length, "file", "capture.wav");
        return await SaveChunk(form, file, ct);
    }
    public async Task<ChunkRecord> SaveChunk(IFormCollection form, IFormFile file, CancellationToken ct)
    {
        string Required(string key) => form[key].ToString() is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing {key}");
        var session = Required("sessionId"); var source = Required("sourceId");
        if (!Regex.IsMatch(session, "^[a-f0-9]{32}$") || !Regex.IsMatch(source, "^[a-zA-Z0-9_]{1,40}$")) throw new InvalidOperationException("Invalid session or source");
        if (!long.TryParse(Required("sequence"), out var sequence) || sequence < 0 || !long.TryParse(Required("startMs"), out var start) || !long.TryParse(Required("endMs"), out var end) || end <= start) throw new InvalidOperationException("Invalid time range or sequence");
        var hash = Required("sha256").ToLowerInvariant();
        if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new InvalidOperationException("Invalid SHA-256");
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == session) == 0) throw new InvalidOperationException("Session not found");
        var existing = await Collection<ChunkRecord>("chunks").Find(x => x.SessionId == session && x.SourceId == source && x.Sequence == sequence).FirstOrDefaultAsync();
        if (existing is not null && existing.Hash != hash) throw new InvalidOperationException("Sequence already has different audio");
        var id = $"{session}-{source}-{sequence}-{hash}";
        var directory = Path.Combine(audioRoot, session, source);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{sequence}-{hash}.wav");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = File.Create(temporary)) await file.CopyToAsync(output, ct);
            await using (var stream = File.OpenRead(temporary))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
                if (actual != hash) throw new InvalidOperationException("Chunk hash mismatch");
                stream.Position = 0;
                var header = new byte[12]; await stream.ReadExactlyAsync(header, ct);
                if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" || Encoding.ASCII.GetString(header, 8, 4) != "WAVE") throw new InvalidOperationException("Expected RIFF/WAVE audio");
            }
            File.Move(temporary, path, false);
        }
        catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var chunk = existing ?? new ChunkRecord { Id = id, SessionId = session, SourceId = source, Sequence = sequence, StartMs = start, EndMs = end, Hash = hash };
        chunk.Path = path;
        await Collection<ChunkRecord>("chunks").ReplaceOneAsync(x => x.Id == id, chunk, new ReplaceOptions { IsUpsert = true }, ct);
        return chunk;
    }
    public async Task<ChunkRecord?> Chunk(string id)
    {
        var chunk = await Collection<ChunkRecord>("chunks").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (chunk is not null) chunk.Path = Path.Combine(audioRoot, chunk.SessionId, chunk.SourceId, $"{chunk.Sequence}-{chunk.Hash}.wav");
        return chunk;
    }
    public string? Audio(string id)
    {
        var parts = id.Split('-');
        if (parts.Length != 4 || !Regex.IsMatch(parts[0], "^[a-f0-9]{32}$") || !Regex.IsMatch(parts[1], "^[a-zA-Z0-9_]{1,40}$") || !long.TryParse(parts[2], out _) || !Regex.IsMatch(parts[3], "^[a-f0-9]{64}$")) return null;
        var path = Path.Combine(audioRoot, parts[0], parts[1], $"{parts[2]}-{parts[3]}.wav");
        return File.Exists(path) ? path : null;
    }
    public async Task SaveTranscript(ChunkRecord chunk, string original)
    {
        if (await Collection<Transcript>("transcripts").CountDocumentsAsync(x => x.Id == chunk.Id) == 0)
            await Collection<Transcript>("transcripts").InsertOneAsync(new Transcript { Id = chunk.Id, SessionId = chunk.SessionId, SourceId = chunk.SourceId, StartMs = chunk.StartMs, EndMs = chunk.EndMs, Original = original, Uncertain = string.IsNullOrWhiteSpace(original) });
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == chunk.Id,
            Builders<ChunkRecord>.Update.Set(x => x.Status, "transcribed").Set(x => x.Error, null));
    }
    public async Task<bool> HasTranscript(string id) =>
        await Collection<Transcript>("transcripts").CountDocumentsAsync(x => x.Id == id) > 0;
    public async Task SetChunkStatus(string id, string status, string? error = null) =>
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == id,
            Builders<ChunkRecord>.Update.Set(x => x.Status, status).Set(x => x.Error, error));
    public async Task<Transcript> Translate(string sessionId, string transcriptId, string translation)
    {
        var filter = Builders<Transcript>.Filter.Where(x => x.Id == transcriptId && x.SessionId == sessionId);
        var result = await Collection<Transcript>("transcripts").FindOneAndUpdateAsync(filter, Builders<Transcript>.Update.Set(x => x.Translation, translation), new FindOneAndUpdateOptions<Transcript> { ReturnDocument = ReturnDocument.After });
        return result ?? throw new InvalidOperationException("Transcript not found in session");
    }
    public async Task SaveCitations(string sessionId, string questionId, IEnumerable<WebEvidence> evidence)
    {
        var citations = evidence.Select(x => new CitationRecord { Id = Guid.NewGuid().ToString("N"), SessionId = sessionId, QuestionId = questionId, Kind = x.Kind, Url = x.Url, Title = x.Title, StartIndex = x.StartIndex, EndIndex = x.EndIndex }).ToArray();
        if (citations.Length > 0) await Collection<CitationRecord>("citations").InsertManyAsync(citations);
    }
}
