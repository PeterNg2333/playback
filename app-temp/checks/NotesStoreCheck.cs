using Playback.Api.Db;
using Playback.Api.Endpoints;
using System.Text.Json;

internal static class NotesStoreCheck
{
    public static async Task Coverage(string folder) {
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", "mongodb://127.0.0.1:27017");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) throw new InvalidOperationException("Local MongoDB unavailable; no container started");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var id = JsonSerializer.SerializeToElement(await store.CreateSession("E2E note coverage " + Guid.NewGuid().ToString("N")[..8]), options).GetProperty("id").GetString()!;
        var material = JsonSerializer.SerializeToElement(await store.AddMaterial(id, new MaterialInput("Synthetic capacity fixture", "One bottleneck with n=10 gives R/10.")), options).GetProperty("id").GetString()!;
        await store.SaveNote(id, $"## Capacity\n\nR/n for one bottleneck; n=10 gives R/10. [{material}]", "user");
        var before = (await store.Session(id))!;
        try {
            await store.SaveSectionNote(id, new([], [], []), "bad-deletion", before.NoteVersion, before.NoteLanguage);
            throw new Exception("Mongo store accepted an AI deletion");
        } catch (InvalidOperationException) { }
        var unchanged = (await store.Session(id))!;
        if (unchanged.NoteVersion != before.NoteVersion || unchanged.NoteMarkdown != before.NoteMarkdown)
            throw new Exception("Rejected update changed saved notes");
        var update = new Playback.Api.Services.Ai.SectionUpdate(before.CurrentNote!.Sections, before.CurrentNote.Citations, []);
        await store.SaveSectionNote(id, update, "retained-text", before.NoteVersion, before.NoteLanguage);
        var after = (await store.Session(id))!;
        if (after.NoteVersion != 2 || after.NoteMarkdown != before.NoteMarkdown || await store.NoteVersion(id, 1) is null)
            throw new Exception("Accepted retained update lost content or history");
        await File.WriteAllTextAsync(Path.Combine(folder, "store-results.json"), JsonSerializer.Serialize(new {
            testedAt = DateTime.UtcNow, database = "playback_e2e", id, build = typeof(PlaybackStore).Assembly.ManifestModule.ModuleVersionId,
            evidence = "Real local MongoDB; synthetic text only; no external provider, no deletion",
            aiDeletionRejected = true, rejectedHeadUnchanged = true, retainedUpdateReadBack = true, oldVersionRetained = true
        }, options));
        Console.WriteLine("Coverage Mongo checks passed: AI deletion rejected before write, unchanged head, retained update read-back and old history. Synthetic test session retained.");
    }
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
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var sessionId = Json(await store.CreateSession("E2E demo section history " + Guid.NewGuid().ToString("N")[..8])).GetProperty("id").GetString()!;
        var material = Json(await store.AddMaterial(sessionId, new MaterialInput("Fixture material", "A source-backed historical example."))).GetProperty("id").GetString()!;
        await store.SaveNote(sessionId, $"## Earlier example\n\nHistorical source-supported example. [{material}]", "user");
        for (var i = 2; i <= 125; i++) await store.SaveNote(sessionId, $"## Current topic\n\nUser fixture version {i}.", "user");
        var all = new List<int>(); int? before = null;
        do { var page = Json(await store.NotePage(sessionId, before, 20));
            all.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("version").GetInt32()));
            before = page.GetProperty("nextBefore").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextBefore").GetInt32();
        } while (before is not null);
        Check(all.Count == 125 && all.Distinct().Count() == 125 && all.Contains(1), "History paging lost older versions");
        var oldest = await store.NoteVersion(sessionId, 1); Check(oldest?.Citations.Count == 1, "Direct version lookup lost durable citations");
        var current = (await store.Session(sessionId))!;
        await store.RestoreNote(sessionId, 1, current.NoteVersion);
        current = (await store.Session(sessionId))!;
        Check(current.NoteVersion == 126 && current.CurrentNote!.Sections.All(x => x.UserEdited) && current.CurrentNote.Citations.Single().SourceIds.Contains(material),
            "Restore lost citation identity or replaced history");
        try { await store.RestoreNote(sessionId, 2, 125); throw new Exception("Stale restore accepted"); } catch (InvalidOperationException) { }
        var gate = new NoteGateRecord { SessionId = sessionId, InputHash = "fixture", Status = "wait", Decision = "wait" };
        await store.SaveNoteGate(gate); await store.RequestNoteFlush(sessionId); await store.SaveNoteGate(gate);
        Check((await store.NoteGate(sessionId))!.FlushRequested, "Decision save cleared a concurrent Stop flush");
        await store.AcknowledgeNoteFlush(sessionId, 0); Check((await store.NoteGate(sessionId))!.FlushRequested, "An old flush acknowledgement cleared a newer Stop");
        await store.AcknowledgeNoteFlush(sessionId, 1); Check(!(await store.NoteGate(sessionId))!.FlushRequested, "Matching flush was not acknowledged");
        Console.WriteLine($"MongoDB notes store checks passed: 125 paginated versions, direct v1/restore, persistent citations, stale base guard and concurrent Stop flag. Synthetic session retained in playback_e2e: {sessionId}");
    }
}
