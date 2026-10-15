using MongoDB.Driver;
using Playback.Api.Db;
using Playback.Application.Notes;
using Playback.Infrastructure.Notes;
using Playback.Domain.Notes;
using System.Text.Json;

// Opt-in only: existing localhost Mongo, additive synthetic session in playback_e2e, retained for review.
static class RefactorMongoChecks
{
    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_REFACTOR_MONGO") != "yes") throw new InvalidOperationException("Refactor Mongo acceptance requires explicit PLAYBACK_REFACTOR_MONGO=yes");
        var uri = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI") ?? "mongodb://127.0.0.1:27017";
        if (!System.Text.RegularExpressions.Regex.IsMatch(uri, @"^mongodb://(?:127\.0\.0\.1|localhost):\d+(?:[/ ?]|$)")) throw new InvalidOperationException("Refactor acceptance requires an existing localhost Mongo service");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e"); Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var connection = MongoClientSettings.FromConnectionString(uri); connection.ServerSelectionTimeout = TimeSpan.FromSeconds(2); connection.ConnectTimeout = TimeSpan.FromSeconds(2); connection.SocketTimeout = TimeSpan.FromSeconds(10);
        var client = new MongoClient(connection); var protectedDb = client.GetDatabase("playback_prototype"); var db = client.GetDatabase("playback_e2e");
        async Task<Dictionary<string, long>> Counts() {var result = new Dictionary<string, long>(); foreach (var name in await (await protectedDb.ListCollectionNamesAsync()).ToListAsync()) result[name] = await protectedDb.GetCollection<MongoDB.Bson.BsonDocument>(name).CountDocumentsAsync(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty); return result;}
        var before = await Counts(); var store = new PlaybackStore(); if (!await store.IsReady()) throw new InvalidOperationException("Existing localhost MongoDB is unavailable; no service started");
        await store.EnsureRecoveryIndexes(default);
        var json = JsonSerializer.SerializeToElement(await store.CreateSession("Refactor isolated CAS/restart acceptance"), new JsonSerializerOptions(JsonSerializerDefaults.Web)); var id = json.GetProperty("id").GetString()!;
        await store.SaveNote(id, "## Original\n\nRetain $R/n$, code and its condition.", "user", 0);
        var latest = (await store.Session(id))!.CurrentNote!;
        var storage = new MongoNoteHistoryStorage(db.GetCollection<NoteHead>("note_heads"), db.GetCollection<Note>("notes")); var fault = new Fault(storage); var journal = new NoteHistoryJournal(fault);
        var next = JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(latest))!; next.Version++; next.InputHash = "refactor-crash-fixture";
        fault.FailAppend = true; await Expect.RejectsAsync(() => journal.Commit(next, latest.Version), "Mongo fixture did not expose append failure");
        var durable = await storage.Head(id); Expect.That(durable!.PendingHistory && durable.Version == 2, "Atomic Mongo head did not retain history intent");
        var restarted = new PlaybackStore(); var recovered = (await restarted.Session(id))!.CurrentNote!;
        Expect.That(recovered.Version == 2 && recovered.Markdown == latest.Markdown && !(await storage.Head(id))!.PendingHistory, "Restart failed to flush/retain Mongo revision");
        var competing = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i => {try {var candidate = JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(recovered))!; candidate.Version = 3; candidate.InputHash = "cas-writer-" + i; await new NoteHistoryJournal(storage).Commit(candidate, 2); return true;} catch (InvalidOperationException) {return false;}}));
        Expect.That(competing.Count(x => x) == 1 && (await restarted.NoteHistory(id)).Count == 3, "Standalone Mongo competing heads/history did not deduplicate");
        var settings = await restarted.SessionProcessingSettings(id); await restarted.SaveSessionProcessingSettings(id, new(settings.Revision, new(OrganizationEnabled: false, NotePresentation: "headings"), settings.InheritedRevision));
        Expect.That(!(await new PlaybackStore().SessionProcessingSettings(id)).Effective.OrganizationEnabled, "Settings override did not survive fresh repository readback");
        var after = await Counts();
        var protectedStable = before.Count == after.Count && before.All(x => after.GetValueOrDefault(x.Key, -1) == x.Value);
        Console.WriteLine($"Refactor Mongo acceptance passed: standalone head CAS, crash/restart history flush, 3 distinct revisions and persisted overrides; synthetic session retained in playback_e2e: {id}. Original database count snapshot stable={protectedStable}; original database was read only (an active recorder can legitimately change counts).");
    }
    sealed class Fault(INoteHistoryStorage inner) : INoteHistoryStorage
    {
        public bool FailAppend;
        public Task<NoteHead?> Head(string id) => inner.Head(id); public Task Initialize(NoteHead seed) => inner.Initialize(seed); public Task<bool> Advance(NoteHead next, int expected) => inner.Advance(next, expected);
        public Task AppendHistory(Note note) => FailAppend ? throw new InvalidOperationException("Injected append failure after head commit") : inner.AppendHistory(note);
        public Task Acknowledge(string id, int version, string hash) => inner.Acknowledge(id, version, hash);
    }
}
