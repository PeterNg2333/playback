using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Microsoft.Extensions.Primitives;

public sealed record SessionView(
    string Id,
    string Title,
    string? GroupId,
    DateTime CreatedAt,
    string NoteMarkdown,
    int NoteVersion,
    long NoteProcessedThroughMs,
    bool TranslationEnabled,
    string TranslationLanguage,
    bool ExternalProcessingConsent,
    List<Material> Materials,
    List<Transcript> Transcripts,
    List<ChunkRecord> Chunks,
    List<TermCandidate> Terms,
    Note? CurrentNote);
public sealed class SessionRecord { [BsonId] public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string? GroupId { get; set; } public DateTime CreatedAt { get; set; } public bool TranslationEnabled { get; set; } public string TranslationLanguage { get; set; } = "zh-Hant"; public DateTime? TranslationConsentAt { get; set; } public DateTime? ExternalConsentAt { get; set; } }
public sealed class GroupRecord { [BsonId] public string Id { get; set; } = ""; public string Name { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public sealed class Material { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string Name { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class Note { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public int Version { get; set; } public int? BasedOnVersion { get; set; } public string Markdown { get; set; } = ""; public string Author { get; set; } = ""; public long ProcessedThroughMs { get; set; } public DateTime CreatedAt { get; set; } public List<string> TranscriptIds { get; set; } = []; public List<string> MaterialIds { get; set; } = []; public string? InputHash { get; set; } public DateTime? SourceFrom { get; set; } public DateTime? SourceThrough { get; set; } }
public sealed class Transcript
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Original { get; set; } = "";
    public string? Translation { get; set; }
    public string? TranslationLanguage { get; set; }
    public string TranslationStatus { get; set; } = "pending";
    public int TranslationAttempts { get; set; }
    public DateTime? TranslationRetryAt { get; set; }
    public string? TranslationError { get; set; }
    public string NoteStatus { get; set; } = "pending";
    public int NoteAttempts { get; set; }
    public DateTime? NoteRetryAt { get; set; }
    public string? NoteError { get; set; }
    public string? Revision { get; set; }
    public bool Uncertain { get; set; }
    public string RecognitionStatus { get; set; } = "recognized";
}
public sealed class CitationRecord { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string QuestionId { get; set; } = ""; public string Kind { get; set; } = "web"; public string Url { get; set; } = ""; public string Title { get; set; } = ""; public int StartIndex { get; set; } public int EndIndex { get; set; } public bool Private { get; set; } = true; }
public sealed class TranslationVersion { [BsonId] public string Id { get; set; } = ""; public string SessionId { get; set; } = ""; public string TranscriptId { get; set; } = ""; public string Language { get; set; } = ""; public string Text { get; set; } = ""; public string OriginalHash { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public sealed class ChunkRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long Sequence { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Hash { get; set; } = "";
    public string Status { get; set; } = "pending-asr";
    public string? Error { get; set; }
    [BsonIgnore] public string Path { get; set; } = "";
}

public sealed class PlaybackStore
{
    public static bool NeedsReview(string original) => Regex.IsMatch(original, @"\[(?:unclear|inaudible|unintelligible)\]", RegexOptions.IgnoreCase);
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
    public async Task<object> CreateSession(string title, string? groupId = null)
    {
        title = title.Trim();
        if (title.Length is < 1 or > 120) throw new InvalidOperationException("Title must be 1–120 characters");
        if (groupId is not null && await Collection<GroupRecord>("groups").CountDocumentsAsync(x => x.Id == groupId) == 0)
            throw new InvalidOperationException("Group not found");
        var item = new SessionRecord { Id = Guid.NewGuid().ToString("N"), Title = title, GroupId = groupId, CreatedAt = DateTime.UtcNow };
        await Collection<SessionRecord>("sessions").InsertOneAsync(item);
        return new { item.Id, item.Title, item.GroupId };
    }
    public async Task<List<SessionRecord>> Sessions() => await Collection<SessionRecord>("sessions").Find(FilterDefinition<SessionRecord>.Empty).SortByDescending(x => x.CreatedAt).Limit(200).ToListAsync();
    public async Task<List<GroupRecord>> Groups() => await Collection<GroupRecord>("groups").Find(FilterDefinition<GroupRecord>.Empty).SortBy(x => x.CreatedAt).ToListAsync();
    public async Task<GroupRecord> CreateGroup(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new InvalidOperationException("Group name must be 1–80 characters");
        var group = new GroupRecord { Id = Guid.NewGuid().ToString("N"), Name = name, CreatedAt = DateTime.UtcNow };
        await Collection<GroupRecord>("groups").InsertOneAsync(group);
        return group;
    }
    public async Task<GroupRecord> RenameGroup(string id, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new InvalidOperationException("Group name must be 1–80 characters");
        return await Collection<GroupRecord>("groups").FindOneAndUpdateAsync(x => x.Id == id,
            Builders<GroupRecord>.Update.Set(x => x.Name, name),
            new FindOneAndUpdateOptions<GroupRecord> { ReturnDocument = ReturnDocument.After })
            ?? throw new InvalidOperationException("Group not found");
    }
    public async Task<object> MoveSession(string id, string? groupId)
    {
        if (groupId is not null && await Collection<GroupRecord>("groups").CountDocumentsAsync(x => x.Id == groupId) == 0)
            throw new InvalidOperationException("Group not found");
        var result = await Collection<SessionRecord>("sessions").UpdateOneAsync(x => x.Id == id,
            Builders<SessionRecord>.Update.Set(x => x.GroupId, groupId));
        if (result.MatchedCount == 0) throw new InvalidOperationException("Session not found");
        return new { id, groupId };
    }
    public async Task<SessionRecord> SetTranslation(string id, bool enabled, string language, bool consentConfirmed)
    {
        if (language is not ("zh-Hant" or "en" or "ja" or "ko")) throw new InvalidOperationException("Unsupported translation language");
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Session not found");
        if (enabled && session.ExternalConsentAt is null)
            throw new InvalidOperationException("Confirm external processing consent before enabling translation");
        if (enabled && !consentConfirmed && session.TranslationConsentAt is null)
            throw new InvalidOperationException("Confirm recording consent before sending transcript to Gemini");
        var changedLanguage = session.TranslationLanguage != language;
        var update = Builders<SessionRecord>.Update.Set(x => x.TranslationEnabled, enabled).Set(x => x.TranslationLanguage, language);
        if (enabled && consentConfirmed) update = update.Set(x => x.TranslationConsentAt, DateTime.UtcNow);
        await Collection<SessionRecord>("sessions").UpdateOneAsync(x => x.Id == id, update);
        if (changedLanguage)
            await Collection<Transcript>("transcripts").UpdateManyAsync(x => x.SessionId == id,
                Builders<Transcript>.Update.Set(x => x.TranslationStatus, "pending").Set(x => x.TranslationRetryAt, null).Set(x => x.TranslationError, null));
        return (await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstAsync());
    }
    public async Task<SessionRecord> SetExternalConsent(string id, bool confirmed)
    {
        var update = Builders<SessionRecord>.Update.Set(x => x.ExternalConsentAt, confirmed ? DateTime.UtcNow : null);
        if (!confirmed) update = update.Set(x => x.TranslationEnabled, false);
        var session = await Collection<SessionRecord>("sessions").FindOneAndUpdateAsync(x => x.Id == id, update,
            new FindOneAndUpdateOptions<SessionRecord> { ReturnDocument = ReturnDocument.After })
            ?? throw new InvalidOperationException("Session not found");
        if (confirmed)
            await Collection<ChunkRecord>("chunks").UpdateManyAsync(x => x.SessionId == id && x.Status == "awaiting-consent",
                Builders<ChunkRecord>.Update.Set(x => x.Status, "pending-asr"));
        return session;
    }
    public async Task<bool> HasExternalConsent(string id) =>
        await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == id && x.ExternalConsentAt != null) > 0;
    public async Task<List<Transcript>> PendingTranslations(int limit = 30, string? sessionId = null)
    {
        var sessions = await Collection<SessionRecord>("sessions").Find(x => x.TranslationEnabled && x.TranslationConsentAt != null && x.ExternalConsentAt != null && (sessionId == null || x.Id == sessionId)).ToListAsync();
        var result = new List<Transcript>();
        foreach (var session in sessions)
        {
            var transcripts = await Collection<Transcript>("transcripts").Find(x => x.SessionId == session.Id && x.Original != "" &&
                (x.TranslationLanguage != session.TranslationLanguage || x.TranslationStatus != "completed") &&
                (x.TranslationRetryAt == null || x.TranslationRetryAt <= DateTime.UtcNow))
                .SortBy(x => x.StartMs).Limit(limit - result.Count).ToListAsync();
            result.AddRange(transcripts);
            if (result.Count >= limit) break;
        }
        return result;
    }
    public async Task RetryTranslations(string id) =>
        await Collection<Transcript>("transcripts").UpdateManyAsync(x => x.SessionId == id && x.TranslationStatus == "failed",
            Builders<Transcript>.Update.Set(x => x.TranslationRetryAt, null).Set(x => x.TranslationStatus, "pending"));
    public async Task<Transcript> SetTranslationResult(Transcript transcript, string language, string? value, string? error)
    {
        if (error is null && value is not null)
        {
            var originalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript.Original))).ToLowerInvariant();
            var versionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{transcript.SessionId}\n{transcript.Id}\n{language}\n{value}"))).ToLowerInvariant();
            var version = new TranslationVersion { Id = versionHash, SessionId = transcript.SessionId, TranscriptId = transcript.Id,
                Language = language, Text = value, OriginalHash = originalHash, CreatedAt = DateTime.UtcNow };
            try { await Collection<TranslationVersion>("translation_versions").InsertOneAsync(version); }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
        }
        var attempts = transcript.TranslationAttempts + 1;
        var update = Builders<Transcript>.Update.Set(x => x.TranslationStatus, error is null ? "completed" : "failed")
            .Set(x => x.TranslationAttempts, attempts).Set(x => x.TranslationError, error)
            .Set(x => x.TranslationRetryAt, error is null ? null : DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(attempts - 1, 6)))));
        if (error is null) update = update.Set(x => x.Translation, value).Set(x => x.TranslationLanguage, language);
        return await Collection<Transcript>("transcripts").FindOneAndUpdateAsync(x => x.Id == transcript.Id, update,
            new FindOneAndUpdateOptions<Transcript> { ReturnDocument = ReturnDocument.After })
            ?? throw new InvalidOperationException("Transcript not found");
    }
    public async Task MarkNotes(IEnumerable<string> ids, string status, string? error = null)
    {
        var list = ids.ToArray();
        if (list.Length == 0) return;
        var update = Builders<Transcript>.Update.Set(x => x.NoteStatus, status).Set(x => x.NoteError, error)
            .Set(x => x.NoteRetryAt, status == "failed" ? DateTime.UtcNow.AddMinutes(1) : status == "processing" ? DateTime.UtcNow.AddMinutes(5) : null);
        if (status == "failed") update = update.Inc(x => x.NoteAttempts, 1);
        await Collection<Transcript>("transcripts").UpdateManyAsync(x => list.Contains(x.Id), update);
    }
    public async Task<List<string>> SessionsWithPendingNotes()
    {
        var transcripts = await Collection<Transcript>("transcripts").Find(x => x.Original != "" &&
            (x.NoteStatus == "pending" || (x.NoteStatus == "failed" && x.NoteRetryAt <= DateTime.UtcNow) ||
             (x.NoteStatus == "processing" && x.NoteRetryAt <= DateTime.UtcNow))).ToListAsync();
        return transcripts.Select(x => x.SessionId).Distinct().ToList();
    }
    public async Task<object> SaveGeneratedNote(string id, string markdown, List<Transcript> transcripts, List<Material> materials, string inputHash)
    {
        if (markdown.Length is < 1 or > 250_000) throw new InvalidOperationException("Generated note size is invalid");
        var existing = await Collection<Note>("notes").Find(x => x.SessionId == id && x.InputHash == inputHash).FirstOrDefaultAsync();
        if (existing is not null) return new { existing.Version, existing.Markdown, existing.Author };
        var previous = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
        var times = transcripts.Select(x => x.RecordedAt ?? DateTime.MinValue).Where(x => x != DateTime.MinValue).ToArray();
        var note = new Note { Id = $"{id}-{inputHash}", SessionId = id, Version = (previous?.Version ?? 0) + 1, BasedOnVersion = previous?.Version,
            Markdown = markdown, Author = "agent", ProcessedThroughMs = Math.Max(previous?.ProcessedThroughMs ?? 0, transcripts.Max(x => x.EndMs)),
            CreatedAt = DateTime.UtcNow, TranscriptIds = (previous?.TranscriptIds ?? []).Concat(transcripts.Select(x => x.Id)).Distinct().ToList(),
            MaterialIds = (previous?.MaterialIds ?? []).Concat(materials.Select(x => x.Id)).Distinct().ToList(),
            InputHash = inputHash, SourceFrom = times.Length > 0 ? new[] { previous?.SourceFrom ?? times.Min(), times.Min() }.Min() : previous?.SourceFrom,
            SourceThrough = times.Length > 0 ? new[] { previous?.SourceThrough ?? times.Max(), times.Max() }.Max() : previous?.SourceThrough };
        try { await Collection<Note>("notes").InsertOneAsync(note); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var saved = await Collection<Note>("notes").Find(x => x.Id == note.Id).FirstAsync();
            return new { saved.Version, saved.Markdown, saved.Author };
        }
        return new { note.Version, note.Markdown, note.Author };
    }
    public async Task DeleteTestSession(string id)
    {
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new InvalidOperationException("Invalid session ID");
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (session is null || !session.Title.StartsWith("E2E demo ", StringComparison.Ordinal))
            throw new InvalidOperationException("Only E2E demo sessions can be removed by this endpoint");
        var directory = Path.GetFullPath(Path.Combine(audioRoot, id));
        if (!directory.StartsWith(audioRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Audio path is outside the test directory");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        var captureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "local-capture"));
        var captureDirectory = Path.GetFullPath(Path.Combine(captureRoot, id));
        if (!captureDirectory.StartsWith(captureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Capture path is outside the test directory");
        if (Directory.Exists(captureDirectory)) Directory.Delete(captureDirectory, recursive: true);
        await Collection<Transcript>("transcripts").DeleteManyAsync(x => x.SessionId == id);
        await Collection<TranslationVersion>("translation_versions").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Note>("notes").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Material>("materials").DeleteManyAsync(x => x.SessionId == id);
        await Collection<CitationRecord>("citations").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ChunkRecord>("chunks").DeleteManyAsync(x => x.SessionId == id);
        await Collection<SessionRecord>("sessions").DeleteOneAsync(x => x.Id == id);
    }
    public async Task DeleteTestGroup(string id)
    {
        var group = await Collection<GroupRecord>("groups").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (group is null || !group.Name.StartsWith("E2E group ", StringComparison.Ordinal))
            throw new InvalidOperationException("Only E2E groups can be removed by this endpoint");
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.GroupId == id) != 0)
            throw new InvalidOperationException("Group still contains sessions");
        await Collection<GroupRecord>("groups").DeleteOneAsync(x => x.Id == id);
    }
    public async Task AddSyntheticTranscript(string sessionId, string chunkId, string text)
    {
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == sessionId).FirstOrDefaultAsync();
        if (session is null || !session.Title.StartsWith("E2E demo ", StringComparison.Ordinal) ||
            (text.Length > 0 && !text.StartsWith("Synthetic ", StringComparison.Ordinal)) || text.Length > 500)
            throw new InvalidOperationException("Only bounded synthetic E2E transcript text is allowed");
        var chunk = await Chunk(chunkId);
        if (chunk is null || chunk.SessionId != sessionId) throw new InvalidOperationException("Test chunk not found in session");
        await SaveTranscript(chunk, text);
    }
    public async Task<SessionView?> Session(string id)
    {
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (session is null) return null;
        var materials = await Collection<Material>("materials").Find(x => x.SessionId == id).ToListAsync();
        var transcripts = await Collection<Transcript>("transcripts").Find(x => x.SessionId == id).SortBy(x => x.StartMs).ToListAsync();
        var chunks = await Collection<ChunkRecord>("chunks").Find(x => x.SessionId == id).SortBy(x => x.StartMs).ToListAsync();
        var note = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
        return new(session.Id, session.Title, session.GroupId, session.CreatedAt, note?.Markdown ?? "", note?.Version ?? 0, note?.ProcessedThroughMs ?? 0, session.TranslationEnabled, session.TranslationLanguage ?? "zh-Hant", session.ExternalConsentAt is not null, materials, transcripts, chunks, TermFinder.Find(materials, transcripts), note);
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
        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"user\n{previous?.Version ?? 0}\n{markdown}"))).ToLowerInvariant();
        var note = new Note { Id = Guid.NewGuid().ToString("N"), SessionId = id, Version = (previous?.Version ?? 0) + 1, BasedOnVersion = previous?.Version, InputHash = userHash,
            Markdown = markdown, Author = author, ProcessedThroughMs = Math.Max(processedThroughMs, previous?.ProcessedThroughMs ?? 0), CreatedAt = DateTime.UtcNow,
            TranscriptIds = previous?.TranscriptIds.ToList() ?? [], MaterialIds = previous?.MaterialIds.ToList() ?? [], SourceFrom = previous?.SourceFrom, SourceThrough = previous?.SourceThrough };
        await Collection<Note>("notes").InsertOneAsync(note);
        return new { note.Version, note.Markdown, note.Author };
    }
    public async Task<List<Note>> NoteHistory(string id)
    {
        var latest = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).Limit(100).ToListAsync();
        latest.Reverse();
        return latest;
    }
    public async Task<ChunkRecord> SaveLocalChunk(string sessionId, string sourceId, long sequence, long startMs, long endMs, string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        stream.Position = 0;
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["sessionId"] = sessionId, ["sourceId"] = sourceId, ["sequence"] = sequence.ToString(CultureInfo.InvariantCulture),
            ["startMs"] = startMs.ToString(CultureInfo.InvariantCulture), ["endMs"] = endMs.ToString(CultureInfo.InvariantCulture), ["sha256"] = hash,
            ["recordedAt"] = File.GetLastWriteTimeUtc(path).AddMilliseconds(-(endMs - startMs)).ToString("O", CultureInfo.InvariantCulture)
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
        var recordedAt = DateTime.TryParse(form["recordedAt"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime() : DateTime.UtcNow.AddMilliseconds(-(end - start));
        var chunk = existing ?? new ChunkRecord { Id = id, SessionId = session, SourceId = source, Sequence = sequence, StartMs = start, EndMs = end, RecordedAt = recordedAt, Hash = hash };
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
    public async Task<List<ChunkRecord>> PendingAsrChunks() => await Collection<ChunkRecord>("chunks")
        .Find(x => x.Status != "transcribed" && x.Status != "asr-empty" && x.Status != "silent" && x.Status != "awaiting-consent").ToListAsync();
    public async Task SaveTranscript(ChunkRecord chunk, string original)
    {
        if (await Collection<Transcript>("transcripts").CountDocumentsAsync(x => x.Id == chunk.Id) == 0)
            await Collection<Transcript>("transcripts").InsertOneAsync(new Transcript { Id = chunk.Id, SessionId = chunk.SessionId, SourceId = chunk.SourceId, StartMs = chunk.StartMs, EndMs = chunk.EndMs, RecordedAt = chunk.RecordedAt, Original = original, Uncertain = NeedsReview(original), RecognitionStatus = string.IsNullOrWhiteSpace(original) ? "asr-empty" : NeedsReview(original) ? "review-needed" : "recognized", NoteStatus = string.IsNullOrWhiteSpace(original) ? "skipped" : "pending" });
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == chunk.Id,
            Builders<ChunkRecord>.Update.Set(x => x.Status, string.IsNullOrWhiteSpace(original) ? "asr-empty" : "transcribed").Set(x => x.Error, null));
    }
    public async Task<Transcript?> TranscriptForChunk(string id) =>
        await Collection<Transcript>("transcripts").Find(x => x.Id == id).FirstOrDefaultAsync();
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
