using System.Text.Json;
using MongoDB.Driver;
using Playback.Api.Db;

// Session sync against local MongoDB (playback_e2e): 900 microphone and system sources page in, idle polling
// queries nothing, each change sends one record, and a restarted API resets old cursors. The session stays for review.
static class SessionSyncStoreChecks
{
    public static async Task Run() {
        var uri = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI") ?? "mongodb://127.0.0.1:27017";
        if (!uri.StartsWith("mongodb://127.0.0.1:") && !uri.StartsWith("mongodb://localhost:")) throw new InvalidOperationException("Store checks require localhost MongoDB");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) { Console.WriteLine("NOT VERIFIED: MongoDB is unavailable. No data was created or removed."); Environment.ExitCode = 2; return; }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, options);
        var id = Json(await store.CreateSession("E2E demo incremental sync " + Guid.NewGuid().ToString("N")[..8])).GetProperty("id").GetString()!;
        await store.SetLanguages(id, "en", "en");
        var db = new MongoClient(uri).GetDatabase("playback_e2e");
        var records = Enumerable.Range(0, 900).Select(i => new Transcript {
            Id = id + "-fixture-" + i, SessionId = id, SourceId = i % 2 == 0 ? "microphone" : "system",
            StartMs = i / 2 * 8000, EndMs = i / 2 * 8000 + 8000, RecordedAt = DateTime.UtcNow.Date.AddMilliseconds(i / 2 * 8000),
            Original = i == 410 ? "Synthetic TCP contains a quantization example." : "Synthetic TCP source " + i + " preserves exact source identity."
        }).ToList();
        await db.GetCollection<Transcript>("transcripts").InsertManyAsync(records);
        await db.GetCollection<ChunkRecord>("chunks").InsertManyAsync(records.Select((x, i) => new ChunkRecord {
            Id = x.Id, SessionId = id, SourceId = x.SourceId, StartMs = x.StartMs, EndMs = x.EndMs, RecordedAt = x.RecordedAt!.Value,
            Sequence = i, Status = "transcribed", Hash = "fixture-only-no-audio"
        }));
        string? cursor = null; var bootstrap = 0; var pages = 0;
        do {
            var page = Json(await store.SyncSession(id, cursor)); cursor = page.GetProperty("cursor").GetString(); pages++;
            Expect.That(System.Text.Encoding.UTF8.GetByteCount(page.GetRawText()) < 410000 && page.GetProperty("changes").GetArrayLength() <= 128, "Mongo bootstrap page was unbounded");
            bootstrap += page.GetProperty("changes").EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "transcript");
            if (!page.GetProperty("hasMore").GetBoolean()) break;
        } while (pages < 100);
        Expect.That(bootstrap == 900, "Mongo bootstrap lost transcript IDs");
        var idle = Json(await store.SyncSession(id, cursor));
        Expect.That(idle.GetProperty("changes").GetArrayLength() == 0 && idle.GetProperty("diagnostics").GetProperty("recordQueries").GetInt32() == 0, "Idle polling queried session records");
        await store.SetTranslationResult(records[410], "en", "One late translation", null);
        var delta = Json(await store.SyncSession(id, cursor));
        Expect.That(delta.GetProperty("changes").GetArrayLength() == 1 && delta.GetProperty("changes")[0].GetProperty("value").GetProperty("id").GetString() == records[410].Id &&
            delta.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1, "One late update re-read a full session or sent unrelated records");
        await store.SetChunkStatus(records[12].Id, "asr-manual", "Synthetic visible failure");
        var chunk = Json(await store.SyncSession(id, cursor)); Expect.That(chunk.GetProperty("changes").GetArrayLength() == 1 && chunk.GetProperty("changes")[0].GetProperty("kind").GetString() == "chunk", "Chunk lifecycle update sent transcript text");
        await store.AddMaterial(id, "New term discovery", "**quantization** appears in an earlier TCP passage.");
        var material = Json(await store.SyncSession(id, cursor));
        var term = material.GetProperty("changes").EnumerateArray().First(x => x.GetProperty("kind").GetString() == "term" && x.GetProperty("value").GetProperty("text").GetString() == "quantization");
        Expect.That(term.GetProperty("value").GetProperty("transcriptIds").EnumerateArray().Any(x => x.GetString() == records[410].Id), "New terms lost earlier exact source matches");
        await store.MarkNotes([records[700].Id], "completed");
        var noteStatus = Json(await store.SyncSession(id, cursor)); Expect.That(noteStatus.GetProperty("changes").GetArrayLength() == 1 && noteStatus.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1, "A note status change queried all source text");
        var restarted = new PlaybackStore(); var reset = Json(await restarted.SyncSession(id, cursor));
        Expect.That(reset.GetProperty("reset").GetBoolean(), "An API restart accepted a stale in-memory cursor");
        Console.WriteLine($"Mongo delta checks passed: 900 mic+system sources in {pages} pages; idle=zero record queries; one exact translation/chunk/note-status delta; incremental new term finds earlier sources; restart resets cursor. Test session retained: {id}");
    }
}
