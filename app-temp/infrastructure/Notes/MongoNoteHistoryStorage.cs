using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Driver;
using Playback.Application.Notes;
using Playback.Domain;
using Playback.Domain.Notes;
namespace Playback.Infrastructure.Notes;
// The head and every history version store their topics and segments as shared parts (NoteParts). Parts are written
// before the head or version that names them, never change, and are cached once read.
public sealed class MongoNoteHistoryStorage(IMongoCollection<BsonDocument> heads, IMongoCollection<BsonDocument> history,
    IMongoCollection<BsonDocument> parts) : INoteHistoryStorage
{
    const int CachedParts = 2048;
    static FilterDefinitionBuilder<BsonDocument> Filter => Builders<BsonDocument>.Filter;
    static ProjectionDefinition<BsonDocument> InputHashOnly => Builders<BsonDocument>.Projection.Include("InputHash");
    static SortDefinition<BsonDocument> Newest => Builders<BsonDocument>.Sort.Descending("Version");
    readonly ConcurrentDictionary<string, BsonDocument> cache = new();

    public async Task<NoteHead?> Head(string id)
    {
        var head = await heads.Find(Filter.Eq("_id", id)).FirstOrDefaultAsync();
        if (head is null) return null;
        return new NoteHead
        {
            Id = id,
            Version = head["Version"].ToInt32(),
            PendingHistory = head.GetValue("PendingHistory", false).ToBoolean(),
            Current = head.GetValue("Current", BsonNull.Value) is BsonDocument current ? (await Read([current]))[0] : null
        };
    }
    public async Task Initialize(NoteHead seed)
    {
        try {await heads.InsertOneAsync(await Store(seed));}
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    }
    public async Task<bool> Advance(NoteHead next, int expectedVersion) => (await heads.ReplaceOneAsync(
        Filter.Eq("_id", next.Id) & Filter.Eq("Version", expectedVersion) & Filter.Ne("PendingHistory", true), await Store(next))).MatchedCount == 1;
    public async Task AppendHistory(Note note)
    {
        var existing = await history.Find(Filter.Eq("SessionId", note.SessionId) & Filter.Eq("Version", note.Version)).Project(InputHashOnly).FirstOrDefaultAsync();
        if (existing is not null)
        {
            if (InputHash(existing) != note.InputHash) throw new InvalidOperationException("A different history revision already exists; current head is retained for inspection");
            return;
        }
        // Deterministic identity makes an interrupted/competing history flush idempotent.
        note.Id = "note_" + ContentHash.Of(note.SessionId + ":" + note.Version);
        try {await history.InsertOneAsync(await Store(note));}
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await history.Find(Filter.Eq("_id", note.Id)).Project(InputHashOnly).FirstOrDefaultAsync();
            if (InputHash(existing) != note.InputHash) throw new InvalidOperationException("History identity conflict; current head is retained");
        }
    }
    public Task Acknowledge(string id, int version, string inputHash) => heads.UpdateOneAsync(
        Filter.Eq("_id", id) & Filter.Eq("Version", version) & Filter.Eq("Current.InputHash", inputHash),
        Builders<BsonDocument>.Update.Set("PendingHistory", false));

    // History reads for the repository.
    public async Task<Note?> Latest(string id) =>
        await history.Find(Filter.Eq("SessionId", id)).Sort(Newest).FirstOrDefaultAsync() is { } note ? (await Read([note]))[0] : null;
    public async Task<List<Note>> Newer(string id, int limit) =>
        await Read(await history.Find(Filter.Eq("SessionId", id)).Sort(Newest).Limit(limit).ToListAsync());
    public async Task<Note?> Version(string id, int version) =>
        await history.Find(Filter.Eq("SessionId", id) & Filter.Eq("Version", version)).FirstOrDefaultAsync() is { } note ? (await Read([note]))[0] : null;
    // Without parts: a note saved whole counts its topics, one with parts carries the count.
    public async Task<List<NoteVersionSummary>> Summaries(string id, int? before, int limit)
    {
        var filter = Filter.Eq("SessionId", id);
        if (before is { } version) filter &= Filter.Lt("Version", version);
        var projection = Builders<BsonDocument>.Projection.Include("Version").Include("BasedOnVersion").Include("Author")
            .Include("CreatedAt").Include("InputHash").Include("SectionCount").Include("Sections._id");
        return (await history.Find(filter).Sort(Newest).Limit(limit).Project(projection).ToListAsync()).Select(x => new NoteVersionSummary(
            x["Version"].ToInt32(),
            x.GetValue("BasedOnVersion", BsonNull.Value) is { IsBsonNull: false } based ? based.ToInt32() : null,
            x.GetValue("Author", "").AsString,
            x.GetValue("CreatedAt", BsonNull.Value) is BsonDateTime created ? created.ToUniversalTime() : default,
            x.GetValue("SectionCount", BsonNull.Value) is { IsBsonNull: false } count ? count.ToInt32()
                : x.GetValue("Sections", BsonNull.Value) is BsonArray sections ? sections.Count : 0,
            InputHash(x))).ToList();
    }

    async Task<BsonDocument> Store(NoteHead head)
    {
        var document = new NoteHead { Id = head.Id, Version = head.Version, PendingHistory = head.PendingHistory }.ToBsonDocument();
        document["Current"] = head.Current is null ? BsonNull.Value : await Store(head.Current);
        return document;
    }

    async Task<BsonDocument> Store(Note note)
    {
        var stored = NoteParts.Store(note);
        var missing = stored.Parts.Where(x => !cache.ContainsKey(x["_id"].AsString)).ToList();
        if (missing.Count > 0)
        {
            try {await parts.InsertManyAsync(missing, new InsertManyOptions { IsOrdered = false });}
            catch (MongoBulkWriteException ex) when (ex.WriteErrors.All(x => x.Category == ServerErrorCategory.DuplicateKey)) { }
            Remember(missing);
        }
        return stored.Note;
    }

    async Task<List<Note>> Read(List<BsonDocument> notes)
    {
        var found = new Dictionary<string, BsonDocument>();
        foreach (var id in notes.SelectMany(NoteParts.PartIds).Distinct())
            if (cache.TryGetValue(id, out var part)) found[id] = part;
        var missing = notes.SelectMany(NoteParts.PartIds).Distinct().Where(x => !found.ContainsKey(x)).ToList();
        if (missing.Count > 0)
        {
            var read = await parts.Find(Filter.In("_id", missing)).ToListAsync();
            foreach (var part in read) found[part["_id"].AsString] = part;
            Remember(read);
        }
        return notes.Select(note => NoteParts.Read(note, hash =>
            found.TryGetValue(NoteParts.PartId(note["SessionId"].AsString, hash), out var part) ? part
                : throw new InvalidOperationException("A saved note topic or segment is missing; the note was not read"))).ToList();
    }

    void Remember(IEnumerable<BsonDocument> read)
    {
        if (cache.Count > CachedParts) cache.Clear();
        foreach (var part in read) cache[part["_id"].AsString] = part;
    }

    static string? InputHash(BsonDocument? document) =>
        document?.GetValue("InputHash", BsonNull.Value) is BsonString hash ? hash.Value : null;
}

public sealed record NoteVersionSummary(int Version, int? BasedOnVersion, string Author, DateTime CreatedAt, int SectionCount, string? InputHash);
