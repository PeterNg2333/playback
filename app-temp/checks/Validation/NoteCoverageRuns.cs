using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Api.Sources;

// The 2026-09-29 note-coverage investigation: replays a saved session through gap repair (offline with the
// fixture model, or Gemini with PLAYBACK_NOTE_COVERAGE_LIVE=yes) and writes the evidence into its run folder.
static class NoteCoverageRuns
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Replay(string folder, bool live)
    {
        if (live && Environment.GetEnvironmentVariable("PLAYBACK_NOTE_COVERAGE_LIVE") != "yes")
            throw new InvalidOperationException("Explicit coverage replay live opt-in required");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", live ? "no" : "yes");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_NOTES", "no");
        var options = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
        var source = JsonSerializer.Deserialize<SessionView>(await File.ReadAllTextAsync(Path.Combine(folder, "session-before.json")), JsonOptions)!;
        var store = new MemoryStore();
        store.Add(source.Id);
        store.Languages[source.Id] = source.NoteLanguage;
        store.Transcripts[source.Id] = source.Transcripts;
        store.Materials[source.Id] = source.Materials;
        if (source.CurrentNote is not null) store.Notes[source.Id] = source.CurrentNote;
        var initial = NoteCoverage.Audit(source);
        var activity = new AiActivity(store);
        GeminiLanguageModel model = live ? new GeminiLanguageModel() : new FixtureNoteModel();
        if (live && !model.IsConfigured) throw new InvalidOperationException("Gemini credential unavailable in process environment");
        await using var agent = new NoteAgent(store, model, new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, activity);
        var steps = new List<object>();
        var errors = new List<string>();
        var max = live ? 3 : 100;
        var previous = source;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(live ? 6 : 3));
        for (var i = 0; i < max && NoteCoverage.Audit(previous).Unreferenced > 0; i++)
        {
            var input = NoteCoverage.OldestBatch(previous);
            try { await agent.RepairCoverage(source.Id, previous.NoteVersion, deadline.Token); }
            catch (Exception ex)
            {
                errors.Add(AiActivity.SafeError(ex));
                break;
            }
            var next = (await store.Session(source.Id))!;
            var retained = NoteSections.Read(previous.CurrentNote, MaterialSources.Allowed(previous)).All(old =>
                next.CurrentNote!.Sections.Any(section => section.Id == old.Id && section.Markdown == old.Markdown));
            Expect.That(retained, "Gap replay changed an existing section");
            var before = NoteCoverage.Audit(previous);
            var after = NoteCoverage.Audit(next);
            steps.Add(new
            {
                step = i + 1, version = next.NoteVersion, inputCount = input.Count, fromMs = input.First().StartMs, throughMs = input.Last().EndMs,
                before = before.Unreferenced, after = after.Unreferenced, existingSectionsRetained = retained
            });
            await File.WriteAllTextAsync(Path.Combine(folder, $"{(live ? "live" : "offline")}-note-v{next.NoteVersion}.json"), JsonSerializer.Serialize(next.CurrentNote, options));
            previous = next;
            if (after.Unreferenced >= before.Unreferenced)
            {
                errors.Add("No citation coverage progress; stopped without automatic paid retry");
                break;
            }
            Console.WriteLine($"{(live ? "Live" : "Offline")} coverage step {i + 1}: v{next.NoteVersion}, {after.Unreferenced} unreferenced, existing sections retained.");
        }
        var final = (await store.Session(source.Id))!;
        var history = JsonSerializer.Deserialize<List<Note>>(await File.ReadAllTextAsync(Path.Combine(folder, "history-before.json")), JsonOptions)!;
        var historyAudit = history.Select(note => new
        {
            note.Version, note.Author, note.CreatedAt,
            bodyCharacters = Regex.Replace(note.Markdown, @"\[[^\[\]\r\n]+\]", "").Length,
            registered = note.TranscriptIds.Count,
            references = NoteCoverage.Audit(source with { NoteMarkdown = note.Markdown, CurrentNote = note }).Referenced
        });
        var report = new
        {
            testedAt = DateTime.UtcNow,
            build = typeof(NoteAgent).Assembly.ManifestModule.ModuleVersionId,
            promptVersion = NoteInstructions.Version,
            model = live ? model.Model : "literal-text fixture",
            evidence = live ? "Real Gemini text calls through production NoteAgent, isolated memory store; not Mongo/API, hardware or new ASR" :
                "Offline deterministic replay of all saved transcript sources; fixture copies input text, not natural AI quality",
            initial,
            final = NoteCoverage.Audit(final),
            steps,
            errors,
            records = store.Activity.Values.ToList(),
            historyAudit
        };
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-results.json" : "offline-results.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-session.json" : "offline-session.json"), JsonSerializer.Serialize(final, options));
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-notes.md" : "offline-notes.md"), final.NoteMarkdown);
        Expect.That(live || report.final.Unreferenced == 0, "Full offline transcript replay left holes");
        if (errors.Count > 0) Environment.ExitCode = 2;
    }

    // Against local MongoDB (playback_e2e): an AI deletion is refused before writing, and the session stays for review.
    public static async Task Store(string folder)
    {
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", "mongodb://127.0.0.1:27017");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) throw new InvalidOperationException("Local MongoDB unavailable; no container started");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var id = JsonSerializer.SerializeToElement(await store.CreateSession("E2E note coverage " + Guid.NewGuid().ToString("N")[..8]), options).GetProperty("id").GetString()!;
        var material = JsonSerializer.SerializeToElement(await store.AddMaterial(id, "Synthetic capacity fixture", "One bottleneck with n=10 gives R/10."), options)
            .GetProperty("id").GetString()!;
        await store.SaveNote(id, $"## Capacity\n\nR/n for one bottleneck; n=10 gives R/10. [{material}]", "user");
        var before = (await store.Session(id))!;
        await Expect.RejectsAsync(() => store.SaveSectionNote(id, new([], [], []), "bad-deletion", before.NoteVersion, before.NoteLanguage),
            "Mongo store accepted an AI deletion");
        var unchanged = (await store.Session(id))!;
        Expect.That(unchanged.NoteVersion == before.NoteVersion && unchanged.NoteMarkdown == before.NoteMarkdown, "Rejected update changed saved notes");
        var update = new SectionUpdate(before.CurrentNote!.Sections, before.CurrentNote.Citations, []);
        await store.SaveSectionNote(id, update, "retained-text", before.NoteVersion, before.NoteLanguage);
        var after = (await store.Session(id))!;
        Expect.That(after.NoteVersion == 2 && after.NoteMarkdown == before.NoteMarkdown && await store.NoteVersion(id, 1) is not null,
            "Accepted retained update lost content or history");
        await File.WriteAllTextAsync(Path.Combine(folder, "store-results.json"), JsonSerializer.Serialize(new
        {
            testedAt = DateTime.UtcNow, database = "playback_e2e", id, build = typeof(PlaybackStore).Assembly.ManifestModule.ModuleVersionId,
            evidence = "Real local MongoDB; synthetic text only; no external provider, no deletion",
            aiDeletionRejected = true, rejectedHeadUnchanged = true, retainedUpdateReadBack = true, oldVersionRetained = true
        }, options));
        Console.WriteLine("Coverage Mongo checks passed: AI deletion rejected before write, unchanged head, retained update read-back and old history. Synthetic test session retained.");
    }
}
