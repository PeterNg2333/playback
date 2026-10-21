using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Playback.Api;
using Playback.Api.Db;

// Data patch for notes saved before commit 8dd747f simplified the note model. The model now reads only
// Sections → Blocks with each block's SourceIds, so two older shapes lost what they showed:
// - placement era: a block's sources were in Blocks[].Anchors[].SourceIds; they become the block's SourceIds.
// - pre-tree: a topic had only Markdown and Points; its Markdown without the leading heading becomes one block.
// Both history versions (notes) and each head's current copy (note_heads.Current) are patched. Nothing is removed:
// the old fields stay in the document, and Note.Markdown is not changed. Running it again changes nothing.
//
// Default is a dry run. With --apply, the original of every changed document is written to app-temp/data/backups/ before
// the document is replaced, and --legacy-note-patch-rollback <file> puts those originals back.
static class LegacyNotePatch
{
    public sealed record Change(int AnchoredBlocks, int TopicsConverted, int EmptyTopics)
    {
        public bool Any => AnchoredBlocks > 0 || TopicsConverted > 0;
    }

    // Changes one stored note in place. allowed is the session's source IDs, as NoteSections.Compact expects.
    public static Change Patch(BsonDocument note, IReadOnlySet<string> allowed)
    {
        var sessionId = note.GetValue("SessionId", "").AsString;
        var citations = Array(note, "Citations").Select(x => BsonSerializer.Deserialize<NoteCitation>(x.AsBsonDocument)).ToList();
        var known = citations.Count;
        int anchored = 0, converted = 0, empty = 0;
        foreach (var topic in Array(note, "Sections").Select(x => x.AsBsonDocument))
        {
            var blocks = Array(topic, "Blocks");
            if (blocks.Count > 0) anchored += blocks.Count(x => AnchorsToSources(x.AsBsonDocument));
            else if (PreTreeBlock(sessionId, topic, allowed, citations) is { } block) { topic["Blocks"] = new BsonArray { block }; converted++; }
            else if (topic.Contains("Markdown")) empty++;
        }
        foreach (var segment in Array(note, "Segments").Select(x => x.AsBsonDocument))
            anchored += Array(segment, "Blocks").Count(x => AnchorsToSources(x.AsBsonDocument));
        if (citations.Count > known)
        {
            if (note.GetValue("Citations", BsonNull.Value) is not BsonArray stored) note["Citations"] = stored = [];
            foreach (var citation in citations.Skip(known)) stored.Add(citation.ToBsonDocument());
        }
        return new(anchored, converted, empty);
    }

    // The union of the block's anchor sources, after any sources it already lists.
    static bool AnchorsToSources(BsonDocument block)
    {
        var anchors = Array(block, "Anchors");
        if (anchors.Count == 0) return false;
        var current = Strings(block, "SourceIds");
        var sources = current.Concat(anchors.SelectMany(x => Strings(x.AsBsonDocument, "SourceIds"))).Distinct(StringComparer.Ordinal).ToList();
        if (sources.SequenceEqual(current)) return false;
        block["SourceIds"] = new BsonArray(sources);
        return true;
    }

    // A pre-tree topic's Markdown without its heading, as one automatic block citing its points' sources.
    static BsonDocument? PreTreeBlock(string sessionId, BsonDocument topic, IReadOnlySet<string> allowed, List<NoteCitation> citations)
    {
        if (topic.GetValue("Markdown", BsonNull.Value) is not BsonString { Value: var markdown }) return null;
        var body = Regex.Replace(markdown, @"\A#{1,4}\s+[^\r\n]+\r?\n\s*\n?", "").Trim();
        if (body.Length == 0) return null;
        var topicId = topic.GetValue("_id", topic.GetValue("Id", "")).AsString;
        var compact = NoteSections.Compact(sessionId, body, allowed, citations);
        return new NoteBlock
        {
            Id = "block_" + Playback.Domain.ContentHash.Of(topicId + ":" + body)[..20],
            Role = "note",
            Body = compact,
            CitationIds = CitationSyntax.Bodies(compact).Distinct().ToList(),
            SourceIds = Array(topic, "Points").SelectMany(x => Strings(x.AsBsonDocument, "SourceIds")).Distinct(StringComparer.Ordinal).ToList()
        }.ToBsonDocument();
    }

    static BsonArray Array(BsonDocument document, string name) => document.GetValue(name, BsonNull.Value) as BsonArray ?? [];
    static List<string> Strings(BsonDocument document, string name) => Array(document, name).Where(x => x.IsString).Select(x => x.AsString).ToList();

    // --legacy-note-patch [--apply] [--session <id>] on PLAYBACK_MONGO_DATABASE (default playback_prototype).
    public static async Task Run(string[] args)
    {
        var apply = false;
        string? session = null;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--apply": apply = true; break;
                case "--session" when i + 1 < args.Length: session = args[++i]; break;
                default: throw new ArgumentException("Usage: --legacy-note-patch [--apply] [--session <id>]");
            }
        var db = await Database();
        if (db is null) return;
        await Run(db, apply, session);
    }

    // The documents written (or, in a dry run, to write) and the backup file of their originals. Documents are read and
    // written one at a time; with apply, each original reaches the backup file before its replacement is written.
    public static async Task<(int Notes, int Heads, string? Backup)> Run(IMongoDatabase db, bool apply, string? session = null)
    {
        var database = db.DatabaseNamespace.DatabaseName;
        Console.WriteLine($"Legacy note patch ({(apply ? "apply" : "dry run")}) on database {database}{(session is null ? "" : ", session " + session)}");
        var notes = db.GetCollection<BsonDocument>("notes");
        var heads = db.GetCollection<BsonDocument>("note_heads");
        var allowed = new Dictionary<string, IReadOnlySet<string>>();
        async Task<IReadOnlySet<string>> Allowed(string id) => allowed.TryGetValue(id, out var ids) ? ids : allowed[id] = await Sources(db, id);
        var survey = new Survey();
        var sessions = new HashSet<string>();
        int notesChanged = 0, headsChanged = 0, skipped = 0;
        await using var backup = apply ? new BackupFile(database) : null;

        async Task<bool> Write(IMongoCollection<BsonDocument> collection, BsonDocument original, BsonDocument patched)
        {
            if (backup is null) return true;
            await backup.Add(collection.CollectionNamespace.CollectionName, original);
            if ((await collection.ReplaceOneAsync(Same(original), patched)).ModifiedCount == 1) return true;
            skipped++;
            return false;
        }

        var byId = Builders<BsonDocument>.Sort.Ascending("_id");
        var noteFilter = session is null ? FilterDefinition<BsonDocument>.Empty : Builders<BsonDocument>.Filter.Eq("SessionId", session);
        await notes.Find(noteFilter).Sort(byId).ForEachAsync(async note =>
        {
            survey.Count("notes", note);
            var sessionId = note.GetValue("SessionId", "").AsString;
            var patched = note.DeepClone().AsBsonDocument;
            var change = Patch(patched, await Allowed(sessionId));
            survey.Add("notes", change);
            if (change.Any && await Write(notes, note, patched)) { notesChanged++; sessions.Add(sessionId); }
        });
        var headFilter = session is null ? FilterDefinition<BsonDocument>.Empty : Builders<BsonDocument>.Filter.Eq("_id", session);
        await heads.Find(headFilter).Sort(byId).ForEachAsync(async head =>
        {
            if (head.GetValue("Current", BsonNull.Value) is not BsonDocument current) { survey.HeadsWithoutCurrent++; return; }
            survey.Count("note_heads.Current", current);
            var sessionId = head["_id"].AsString;
            var patched = head.DeepClone().AsBsonDocument;
            var change = Patch(patched["Current"].AsBsonDocument, await Allowed(sessionId));
            survey.Add("note_heads.Current", change);
            if (change.Any && await Write(heads, head, patched)) { headsChanged++; sessions.Add(sessionId); }
        });

        survey.Print();
        Console.WriteLine($"{(apply ? "Changed" : "To change")}: {notesChanged} notes documents, {headsChanged} note_heads documents, {sessions.Count} sessions. " +
            "Removed fields: none; Note.Markdown unchanged." + (skipped > 0 ? $" {skipped} documents saved again during the run were left; run again." : ""));
        if (backup is { Count: > 0 }) Console.WriteLine($"Originals of {backup.Count} documents saved to {backup.Path}");
        return (notesChanged, headsChanged, backup is { Count: > 0 } ? backup.Path : null);
    }

    // --legacy-note-patch-rollback <file>: puts back the originals a backup holds. A head saved again since the patch is
    // left alone and reported, so a newer version is never replaced by an older one.
    public static async Task Rollback(string file)
    {
        if (await Database() is { } db) await Rollback(db, file);
    }

    public static async Task<(int Restored, int Kept)> Rollback(IMongoDatabase db, string file)
    {
        using var reader = new StreamReader(file);
        var header = BsonDocument.Parse(await reader.ReadLineAsync() ?? "{}");
        if (header.GetValue("Database", "").AsString != db.DatabaseNamespace.DatabaseName)
            throw new InvalidOperationException($"Backup is for database {header.GetValue("Database", "?")}, not {db.DatabaseNamespace.DatabaseName}");
        int restored = 0, kept = 0;
        while (await reader.ReadLineAsync() is { Length: > 0 } line)
        {
            var item = BsonDocument.Parse(line);
            var original = item["Document"].AsBsonDocument;
            var result = await db.GetCollection<BsonDocument>(item["Collection"].AsString).ReplaceOneAsync(Same(original), original);
            if (result.MatchedCount == 1) restored++; else kept++;
        }
        Console.WriteLine($"Rollback on {db.DatabaseNamespace.DatabaseName}: {restored} documents restored, {kept} left because they were saved again or removed.");
        return (restored, kept);
    }

    // A history version never changes; a head saved again since it was read has a new Version and is skipped.
    static FilterDefinition<BsonDocument> Same(BsonDocument original) =>
        Builders<BsonDocument>.Filter.Eq("_id", original["_id"]) & Builders<BsonDocument>.Filter.Eq("Version", original["Version"]);

    // JSON lines under app-temp/data/backups: the database, then { Collection, Document } for each original, in canonical
    // extended JSON so every BSON type reads back exactly. A run that changed nothing leaves no file.
    sealed class BackupFile : IAsyncDisposable
    {
        static readonly JsonWriterSettings Json = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson };
        readonly StreamWriter writer;
        public string Path { get; }
        public int Count { get; private set; }

        public BackupFile(string database)
        {
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "backups"));
            Directory.CreateDirectory(folder);
            Path = System.IO.Path.Combine(folder, $"legacy-note-patch-{database}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.jsonl");
            writer = new StreamWriter(Path);
            writer.WriteLine(new BsonDocument { { "Database", database }, { "CreatedAt", DateTime.UtcNow } }.ToJson(Json));
        }

        public async Task Add(string collection, BsonDocument document)
        {
            await writer.WriteLineAsync(new BsonDocument { { "Collection", collection }, { "Document", document } }.ToJson(Json));
            await writer.FlushAsync();
            Count++;
        }

        public async ValueTask DisposeAsync()
        {
            await writer.DisposeAsync();
            if (Count == 0) File.Delete(Path);
        }
    }

    // Every transcript row (superseded ones too: lineage maps them when read), material and material passage of a session.
    static async Task<IReadOnlySet<string>> Sources(IMongoDatabase db, string sessionId)
    {
        var transcripts = await db.GetCollection<BsonDocument>("transcripts").Find(new BsonDocument("SessionId", sessionId))
            .Project(new BsonDocument("_id", 1)).ToListAsync();
        var materials = await db.GetCollection<Material>("materials").Find(x => x.SessionId == sessionId).ToListAsync();
        return transcripts.Select(x => x["_id"].ToString()!).Concat(materials.Select(x => x.Id))
            .Concat(Playback.Api.Sources.MaterialSources.Passages(materials).Select(x => x.Id)).ToHashSet(StringComparer.Ordinal);
    }

    // Local MongoDB only, and only the two databases the app uses.
    public static async Task<IMongoDatabase?> Database()
    {
        var uri = PlaybackEnvironment.MongoUri;
        if (!uri.StartsWith("mongodb://127.0.0.1:") && !uri.StartsWith("mongodb://localhost:")) throw new InvalidOperationException("The note patch runs on localhost MongoDB only");
        if (PlaybackEnvironment.Database is not ("playback_prototype" or "playback_e2e")) throw new InvalidOperationException("Unknown database " + PlaybackEnvironment.Database);
        Playback.Infrastructure.Storage.DocumentMappings.Register();
        var settings = MongoClientSettings.FromConnectionString(uri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
        var db = new MongoClient(settings).GetDatabase(PlaybackEnvironment.Database);
        try { await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1)); }
        catch (TimeoutException)
        {
            Console.WriteLine($"NOT VERIFIED: MongoDB at {uri} is unavailable; nothing was read or changed.");
            Environment.ExitCode = 2;
            return null;
        }
        return db;
    }

    // What the stored notes look like, for the dry-run report.
    sealed class Survey
    {
        readonly SortedDictionary<string, int> counts = [];
        public int HeadsWithoutCurrent;
        void Inc(string key, int by = 1) => counts[key] = counts.GetValueOrDefault(key) + by;

        public void Count(string collection, BsonDocument note)
        {
            Inc($"{collection}: documents");
            Inc($"{collection}: SchemaVersion {note.GetValue("SchemaVersion", "none")}");
            var topics = Array(note, "Sections").Select(x => x.AsBsonDocument).ToList();
            if (topics.Count == 0 && note.GetValue("Markdown", BsonNull.Value) is BsonString { Value: var markdown } && markdown.Trim().Length > 0) Inc($"{collection}: Markdown only, no topics (not patched)");
            if (topics.Any(x => Array(x, "Blocks").Count == 0 && x.Contains("Markdown"))) Inc($"{collection}: with pre-tree topics");
            if (topics.Concat(Array(note, "Segments").Select(x => x.AsBsonDocument)).SelectMany(x => Array(x, "Blocks"))
                .Any(x => Array(x.AsBsonDocument, "Anchors").Count > 0)) Inc($"{collection}: with anchored blocks");
        }

        public void Add(string collection, Change change)
        {
            if (change.Any) Inc($"{collection}: to change");
            Inc($"{collection}: blocks given anchor sources", change.AnchoredBlocks);
            Inc($"{collection}: pre-tree topics given a block", change.TopicsConverted);
            Inc($"{collection}: pre-tree topics with only a heading (left without blocks)", change.EmptyTopics);
        }

        public void Print()
        {
            foreach (var (key, value) in counts.Where(x => x.Value > 0)) Console.WriteLine($"  {key}: {value}");
            Console.WriteLine($"  note_heads without a current note: {HeadsWithoutCurrent}");
        }
    }
}
