using Playback.Api.Db;
using System.Text.Json;

// Note versions in local MongoDB (playback_e2e): 125 versions page back in full, restore keeps citations,
// a stale base is refused, and a Stop survives a concurrent gate decision. The synthetic session stays for review.
static class NoteStoreChecks
{
    public static async Task Run() {
        var uri = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI");
        if (uri is not null && !uri.StartsWith("mongodb://127.0.0.1:") && !uri.StartsWith("mongodb://localhost:"))
            throw new InvalidOperationException("Offline store checks require localhost MongoDB");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) { Console.WriteLine("NOT VERIFIED: localhost MongoDB unavailable; no service started, no DB changes made."); Environment.ExitCode = 2; return; }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, options);
        var sessionId = Json(await store.CreateSession("E2E demo section history " + Guid.NewGuid().ToString("N")[..8])).GetProperty("id").GetString()!;
        var material = Json(await store.AddMaterial(sessionId, "Fixture material", "A source-backed historical example.")).GetProperty("id").GetString()!;
        await store.SaveNote(sessionId, $"## Earlier example\n\nHistorical source-supported example. [{material}]", "user");
        for (var i = 2; i <= 125; i++) await store.SaveNote(sessionId, $"## Current topic\n\nUser fixture version {i}.", "user");
        var all = new List<int>(); int? before = null;
        do { var page = Json(await store.NotePage(sessionId, before, 20));
            all.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("version").GetInt32()));
            before = page.GetProperty("nextBefore").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextBefore").GetInt32();
        } while (before is not null);
        Expect.That(all.Count == 125 && all.Distinct().Count() == 125 && all.Contains(1), "History paging lost older versions");
        var oldest = await store.NoteVersion(sessionId, 1); Expect.That(oldest?.Citations.Count == 1, "Direct version lookup lost durable citations");
        var current = (await store.Session(sessionId))!;
        await store.RestoreNote(sessionId, 1, current.NoteVersion);
        current = (await store.Session(sessionId))!;
        Expect.That(current.NoteVersion == 126 && current.CurrentNote!.Sections.All(x => x.UserEdited) && current.CurrentNote.Citations.Single().SourceIds.Contains(material),
            "Restore lost citation identity or replaced history");
        try { await store.RestoreNote(sessionId, 2, 125); throw new CheckFailed("Stale restore accepted"); } catch (InvalidOperationException) { }
        var gate = new NoteGateRecord { SessionId = sessionId, InputHash = "fixture", Status = "wait", Decision = "wait" };
        await store.SaveNoteGate(gate); await store.RequestNoteFlush(sessionId); await store.SaveNoteGate(gate);
        Expect.That((await store.NoteGate(sessionId))!.FlushRequested, "Decision save cleared a concurrent Stop flush");
        await store.AcknowledgeNoteFlush(sessionId, 0); Expect.That((await store.NoteGate(sessionId))!.FlushRequested, "An old flush acknowledgement cleared a newer Stop");
        await store.AcknowledgeNoteFlush(sessionId, 1); Expect.That(!(await store.NoteGate(sessionId))!.FlushRequested, "Matching flush was not acknowledged");
        Console.WriteLine($"MongoDB notes store checks passed: 125 paginated versions, direct v1/restore, persistent citations, stale base guard and concurrent Stop flag. Synthetic session retained in playback_e2e: {sessionId}");
    }
}
