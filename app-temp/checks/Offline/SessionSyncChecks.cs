using System.Text.Json;
using Playback.Api.Db;
using Playback.Api.Terms;

// The page's session sync over the in-memory store: a bounded paged first read of a long session, nothing
// sent when idle, one record for one change, a cursor that resets across sessions, fresh term sources.
static class SessionSyncChecks
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Run()
    {
        var store = new MemoryStore();
        store.Add("long");
        store.Add("small");
        for (var i = 0; i < 2700; i++) store.Speech("long", "source-" + i, new string('x', 300));
        store.Notes["long"] = new Note { SessionId = "long", Markdown = new string('長', 200000) };
        string? cursor = null;
        var total = 0;
        var pages = 0;
        var fragmentCount = 0;
        while (true)
        {
            var json = JsonSerializer.Serialize(await store.SyncSession("long", cursor), JsonOptions);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Expect.That(System.Text.Encoding.UTF8.GetByteCount(json) < 410000 && root.GetProperty("changes").GetArrayLength() <= 128, "Bootstrap sync was not bounded");
            total += root.GetProperty("changes").EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "transcript");
            pages++;
            fragmentCount += root.GetProperty("changes").EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "fragment");
            cursor = root.GetProperty("cursor").GetString();
            if (!root.GetProperty("hasMore").GetBoolean()) break;
        }
        Expect.That(total == 2700 && pages > 20 && fragmentCount > 0, "Sync silently truncated a large record or the long transcript");

        using var idle = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("long", cursor), JsonOptions));
        Expect.That(idle.RootElement.GetProperty("changes").GetArrayLength() == 0, "Idle sync re-sent a whole session");
        store.Transcripts["long"][1700].Translation = "One late translation";
        store.Touch("long", "translation", ["source-1700"]);
        using var change = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("long", cursor), JsonOptions));
        Expect.That(change.RootElement.GetProperty("changes").GetArrayLength() == 1, "One changed transcript re-sent unchanged records");
        Expect.That(change.RootElement.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1 && store.SyncReadIds.Single().SequenceEqual(["source-1700"]),
            "A one-record delta read the full session or queried unrelated source IDs");
        using var reset = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("small", cursor), JsonOptions));
        Expect.That(reset.RootElement.GetProperty("reset").GetBoolean(), "Sync cursor mixed sources from another session");

        store.Add("terms");
        store.Speech("terms", "term-source", "TCP explains transport.");
        var term = new TermCandidate("TCP", ["term-source"], [], "TCP explains transport.");
        store.Terms["terms"] = [term];
        var insightId = TermInsight.IdFor("terms", term.Text, term.Context);
        store.Insights["terms"] = [new TermInsight { Id = insightId, SessionId = "terms", Term = "TCP", Context = term.Context, TranscriptIds = ["term-source"] }];
        using var termBoot = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("terms", null), JsonOptions));
        var termCursor = termBoot.RootElement.GetProperty("cursor").GetString();
        store.Materials["terms"].Add(new Material { Id = "new-material", SessionId = "terms", Text = "**TCP** supports the earlier explanation." });
        store.Terms["terms"] = [term with { MaterialIds = ["new-material"] }];
        store.Touch("terms", "material", ["new-material"]);
        using var termDelta = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("terms", termCursor), JsonOptions));
        var updatedInsight = termDelta.RootElement.GetProperty("changes").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "insight");
        Expect.That(updatedInsight.GetProperty("value").GetProperty("materialIds")[0].GetString() == "new-material" &&
            termDelta.RootElement.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1,
            "Incremental term updates left an existing saved explanation's source references stale");

        Console.WriteLine($"Session sync checks passed: 2,700 sources + a 200,000-character Chinese note in {pages} pages / {fragmentCount} bounded fragments; " +
            "idle=0 records; one changed source=1 record; cross-session cursor resets; saved explanations gain new sources");
    }
}
