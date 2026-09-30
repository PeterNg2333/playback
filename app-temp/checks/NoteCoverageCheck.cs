using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Db;
using Playback.Api.Services.Ai;
using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;

internal static partial class NotesRedesignCheck
{
    static async Task CoverageSafeguards() {
        var store = new MemoryStore(); store.Add("gaps");
        for (var i = 0; i < 48; i++) {
            store.Speech("gaps", "early-" + i, "A substantive example of capacity sharing.");
            store.Transcripts["gaps"][i].NoteStatus = "completed";
        }
        store.Transcripts["gaps"][12].NoteStatus = "suppressed";
        store.Transcripts["gaps"][13].NoteStatus = "deferred";
        store.Notes["gaps"] = new Note { SessionId = "gaps", Version = 10, Markdown = "## Later\n\nLater fact. [early-47]",
            TranscriptIds = store.Transcripts["gaps"].Select(x => x.Id).ToList(), SuppressedSourceIds = ["early-11"] };
        var session = (await store.Session("gaps"))!;
        var audit = NoteCoverage.Audit(session);
        Check(audit.Referenced == 1 && audit.Suppressed == 2 && audit.Unreferenced == 45 && audit.CompletedWithoutReference == 44 && audit.LargeGaps == 1,
            "Legacy completed metadata, deferral or intentional exclusion hid missing body references");
        Check(NoteCoverage.OldestBatch(session).All(x => x.Id is not ("early-11" or "early-12" or "early-47")), "Repair resurrected an intentional exclusion");
        var model = new FixtureModel(); var activity = new AiActivity(store);
        await using var agent = new NoteAgent(store, model, new FixtureGate(), NullLogger<NoteAgent>.Instance, activity);
        await agent.RepairCoverage("gaps", 10, CancellationToken.None);
        var repaired = (await store.Session("gaps"))!;
        Check(repaired.NoteMarkdown.Contains("Later fact.") && NoteCoverage.Audit(repaired).Unreferenced < 45,
            "Repair failed to retain the latest content or bypass old completed statuses");
        try { await agent.RepairCoverage("gaps", 10, CancellationToken.None); throw new Exception("Stale repair accepted"); } catch (InvalidOperationException) { }

        var prior = new NoteSection { Id = "formula", Title = "Capacity", Markdown = "## Capacity\n\nR/n for n flows; n=10 yields R/10, assuming one shared bottleneck. [a, b]",
            Points = [new NotePoint { Id = "proof", Text = "R/n for n flows; n=10 yields R/10, assuming one shared bottleneck. [a, b]", SourceIds = ["a", "b"] }] };
        var note = new Note { SessionId = "s", Sections = [prior] };
        var deceptive = new NotePatch { Sections = [new SectionPatch { Id = "formula", BaseVersion = 1, Title = "Capacity",
            Markdown = "## Capacity\n\nFlows share capacity. [b, a]", Points = [new PointPatch { Text = "Flows share capacity. [b, a]", SourceIds = ["a", "b"], Retains = ["proof"] }] }] };
        var guarded = NoteSections.Apply("s", note, [prior], deceptive, [prior.Id], [], ["a", "b"], "organize", allowUserEdited: true);
        Check(NoteSections.Render(guarded.Sections).Contains("n=10 yields R/10, assuming one shared bottleneck"),
            "Organizer claimed a retained ID and citations while deleting a formula/condition");
        Reject(() => NoteSections.RequireRetention(note, new SectionUpdate([], [], []), ["a", "b"]), "Persistence boundary accepted deleting a whole saved section");
        NoteSections.RequireRetention(note, guarded, ["a", "b"]);
        var reordered = new NotePatch { Sections = [new SectionPatch { Id = "formula", BaseVersion = 1, Title = "Capacity",
            Markdown = prior.Markdown.Replace("[a, b]", "[b, a]"), Points = [new PointPatch { Text = prior.Points[0].Text.Replace("[a, b]", "[b, a]"), SourceIds = ["a", "b"], Retains = ["proof"] }] }] };
        var canonical = NoteSections.Apply("s", note, [prior], reordered, [prior.Id], [], ["a", "b"], "reorder");
        Check(canonical.Sections[0].Points.Count == 1 && !canonical.Sections[0].Markdown.Contains("Earlier source-backed points"),
            "Citation reordering duplicated an unchanged old point");
    }

    public static async Task CoverageReplay(string folder, bool live) {
        if (live && Environment.GetEnvironmentVariable("PLAYBACK_NOTE_COVERAGE_LIVE") != "yes")
            throw new InvalidOperationException("Explicit coverage replay live opt-in required");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", live ? "no" : "yes");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_NOTES", "no");
        var options = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
        var source = JsonSerializer.Deserialize<SessionView>(await File.ReadAllTextAsync(Path.Combine(folder, "session-before.json")), JsonOptions)!;
        var store = new MemoryStore(); store.Add(source.Id); store.Languages[source.Id] = source.NoteLanguage;
        store.Transcripts[source.Id] = source.Transcripts; store.Materials[source.Id] = source.Materials;
        if (source.CurrentNote is not null) store.Notes[source.Id] = source.CurrentNote;
        var initial = NoteCoverage.Audit(source); var activity = new AiActivity(store);
        GeminiLanguageModel model = live ? new GeminiLanguageModel() : new FixtureModel();
        if (live && !model.IsConfigured) throw new InvalidOperationException("Gemini credential unavailable in process environment");
        await using var agent = new NoteAgent(store, model, new FixtureGate(), NullLogger<NoteAgent>.Instance, activity);
        var steps = new List<object>(); var errors = new List<string>();
        var max = live ? 3 : 100; var previous = source;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(live ? 6 : 3));
        for (var i = 0; i < max && NoteCoverage.Audit(previous).Unreferenced > 0; i++) {
            var input = NoteCoverage.OldestBatch(previous);
            try { await agent.RepairCoverage(source.Id, previous.NoteVersion, deadline.Token); }
            catch (Exception ex) { errors.Add(AiActivity.SafeError(ex)); break; }
            var next = (await store.Session(source.Id))!;
            var retained = NoteSections.Read(previous.CurrentNote, MaterialSources.Allowed(previous)).All(old =>
                next.CurrentNote!.Sections.Any(section => section.Id == old.Id && section.Markdown == old.Markdown));
            Check(retained, "Gap replay changed an existing section");
            var before = NoteCoverage.Audit(previous); var after = NoteCoverage.Audit(next);
            steps.Add(new { step = i + 1, version = next.NoteVersion, inputCount = input.Count, fromMs = input.First().StartMs, throughMs = input.Last().EndMs,
                before = before.Unreferenced, after = after.Unreferenced, existingSectionsRetained = retained });
            await File.WriteAllTextAsync(Path.Combine(folder, $"{(live ? "live" : "offline")}-note-v{next.NoteVersion}.json"), JsonSerializer.Serialize(next.CurrentNote, options));
            previous = next;
            if (after.Unreferenced >= before.Unreferenced) { errors.Add("No citation coverage progress; stopped without automatic paid retry"); break; }
            Console.WriteLine($"{(live ? "Live" : "Offline")} coverage step {i + 1}: v{next.NoteVersion}, {after.Unreferenced} unreferenced, existing sections retained.");
        }
        var final = (await store.Session(source.Id))!;
        var history = JsonSerializer.Deserialize<List<Note>>(await File.ReadAllTextAsync(Path.Combine(folder, "history-before.json")), JsonOptions)!;
        var historyAudit = history.Select(note => new { note.Version, note.Author, note.CreatedAt,
            bodyCharacters = Regex.Replace(note.Markdown, @"\[[^\[\]\r\n]+\]", "").Length,
            registered = note.TranscriptIds.Count, references = NoteCoverage.Audit(source with { NoteMarkdown = note.Markdown, CurrentNote = note }).Referenced });
        var report = new { testedAt = DateTime.UtcNow, build = typeof(NoteAgent).Assembly.ManifestModule.ModuleVersionId,
            promptVersion = NoteAgent.PromptVersion, model = live ? model.Model : "literal-text fixture",
            evidence = live ? "Real Gemini text calls through production NoteAgent, isolated memory store; not Mongo/API, hardware or new ASR" :
                "Offline deterministic replay of all saved transcript sources; fixture copies input text, not natural AI quality",
            initial, final = NoteCoverage.Audit(final), steps, errors, records = store.Activity.Values.ToList(), historyAudit };
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-results.json" : "offline-results.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-session.json" : "offline-session.json"), JsonSerializer.Serialize(final, options));
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-notes.md" : "offline-notes.md"), final.NoteMarkdown);
        Check(live || report.final.Unreferenced == 0, "Full offline transcript replay left holes");
        if (errors.Count > 0) Environment.ExitCode = 2;
    }
}
