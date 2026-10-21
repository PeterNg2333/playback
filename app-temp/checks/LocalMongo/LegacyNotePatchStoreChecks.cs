using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Playback.Api.Db;

// The legacy note patch against local MongoDB (playback_e2e): a synthetic session with a pre-tree and a placement-era
// version (history and head) is patched once, a second run writes nothing, the patched version restores with its
// sources, and rollback puts the history back while leaving the newer head. The session stays for review.
static class LegacyNotePatchStoreChecks
{
    public static async Task Run()
    {
        var uri = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI");
        if (uri is not null && !uri.StartsWith("mongodb://127.0.0.1:") && !uri.StartsWith("mongodb://localhost:"))
            throw new InvalidOperationException("Offline store checks require localhost MongoDB");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) { Console.WriteLine("NOT VERIFIED: localhost MongoDB unavailable; no service started, no DB changes made."); Environment.ExitCode = 2; return; }
        var db = (await LegacyNotePatch.Database())!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var id = JsonSerializer.SerializeToElement(await store.CreateSession("E2E demo legacy note patch " + Guid.NewGuid().ToString("N")[..8]), options).GetProperty("id").GetString()!;
        string[] t = [id + "-t1", id + "-t2", id + "-t3"];
        await db.GetCollection<Transcript>("transcripts").InsertManyAsync(t.Select((x, i) => new Transcript
            { Id = x, SessionId = id, SourceId = "microphone", StartMs = i * 8000, EndMs = (i + 1) * 8000, Original = "Synthetic legacy source " + i }));
        string Identity(int version) => "note_" + Playback.Domain.ContentHash.Of(id + ":" + version);
        var placement = LegacyNotePatchChecks.Placement(id, Identity(2), t);
        await db.GetCollection<BsonDocument>("notes").InsertManyAsync([LegacyNotePatchChecks.PreTree(id, Identity(1), t), placement]);
        await db.GetCollection<BsonDocument>("note_heads").InsertOneAsync(new BsonDocument
            { { "_id", id }, { "Version", 2 }, { "Current", placement.DeepClone() }, { "PendingHistory", false } });
        await Expect.RejectsAsync(() => store.RestoreNote(id, 1, 2), "Fixture: an unpatched pre-tree version was restorable");

        var first = await LegacyNotePatch.Run(db, apply: true, session: id);
        Expect.That(first is (2, 1, not null), "The patch did not write both history versions and the head once");
        var again = await LegacyNotePatch.Run(db, apply: true, session: id);
        Expect.That(again is (0, 0, null), "A second run wrote documents again");

        var current = (await store.Session(id))!.CurrentNote!;
        Expect.That(current.Sections.Single().Blocks.Single().SourceIds.SequenceEqual(t), "The head's block did not read its anchor sources");
        await store.RestoreNote(id, 1, 2);
        var restored = (await store.Session(id))!.CurrentNote!;
        Expect.That(restored.Version == 3 && restored.Sections.SelectMany(x => x.Blocks).Single().SourceIds.SequenceEqual(t), "The patched pre-tree version did not restore with its sources");

        var (restoredDocuments, kept) = await LegacyNotePatch.Rollback(db, first.Backup!);
        var history = await db.GetCollection<BsonDocument>("notes").Find(new BsonDocument { { "SessionId", id }, { "Version", 1 } }).SingleAsync();
        Expect.That(restoredDocuments == 2 && kept == 1 && !history["Sections"][0].AsBsonDocument.Contains("Blocks") &&
            (await store.Session(id))!.NoteVersion == 3, "Rollback did not restore history only, or replaced the newer head");
        Console.WriteLine($"MongoDB legacy note patch checks passed: history and head patched once, second run unchanged, restore after patch, rollback keeps the newer head. Synthetic session retained in playback_e2e: {id}");
    }
}
