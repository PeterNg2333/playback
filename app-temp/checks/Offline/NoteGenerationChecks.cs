using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;

// Note generation runs, with a fixture model and gate: organizing one protected section, a Stop that waits
// for late speech, deferred and partly deferred replies, rendered citations, bounded input, and the live draft.
static class NoteGenerationChecks
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Run()
    {
        LiveDraft();
        Expect.That(!NoteAgent.ShouldOrganize(new NoteSection
            {
                UserEdited = true,
                Markdown = new string('x', 10000),
                Points = Enumerable.Range(0, 20).Select(_ => new NotePoint()).ToList()
            }),
            "Automatic organization included protected user content");

        var store = new MemoryStore();
        var clock = new TestClock();
        var model = new FixtureNoteModel();
        var gate = new FixtureNoteGate();
        await using var agent = new NoteAgent(store, model, gate, NullLogger<NoteAgent>.Instance, new AiActivity(store), clock);
        async Task Tick(string id)
        {
            clock.Advance(10);
            agent.Tick([id], CancellationToken.None);
            await agent.Drain();
        }

        store.Add("organize");
        store.Speech("organize", "math", "R is the shared capacity; ten flows target R/10.");
        store.Transcripts["organize"][0].NoteStatus = "completed";
        var selected = new NoteSection
        {
            Id = "fair",
            Title = "Fair sharing",
            UserEdited = true,
            Markdown = "## Fair sharing\n\nUser annotation: R/n; n=10 gives R/10. [math]",
            Points = [new NotePoint { Id = "formula", Text = "User annotation: R/n; n=10 gives R/10. [math]", SourceIds = ["math"] }]
        };
        var untouched = new NoteSection { Id = "security", Title = "Security", Markdown = "## Security\n\nUser safety note.", UserEdited = true };
        store.Notes["organize"] = new Note { SessionId = "organize", Version = 7, Sections = [selected, untouched], Markdown = NoteSections.Render([selected, untouched]) };
        await agent.Organize("organize", "fair", 7, CancellationToken.None);
        Expect.That(store.Notes["organize"].Version == 8 && store.Notes["organize"].Sections[1].Markdown == untouched.Markdown &&
            store.Notes["organize"].Sections[0].Markdown.Contains("R/n") && store.Notes["organize"].Sections[0].UserEdited && gate.Calls == 0,
            "Explicit section organization lost the formula, another section, protection or called Jev");
        var call = store.Activity.Values.Single();
        Expect.That(call.SectionId == "fair" && call.PromptVersion == NoteInstructions.OrganizeVersion && call.PromptText!.Contains(NoteInstructions.Organize) &&
            call.InputHash!.Length == 64 && call.UsageJson is not null && call.Status == "completed", "Organization prompt/lifecycle was not the actual request");
        await Expect.RejectsAsync(() => agent.Organize("organize", "fair", 7, CancellationToken.None), "Stale organization base accepted");
        model.BeforeFinish = _ =>
        {
            store.Notes["organize"].Version = 9;
            return Task.CompletedTask;
        };
        await Expect.RejectsAsync(() => agent.Organize("organize", "fair", 8, CancellationToken.None), "Concurrent user version overwritten");
        Expect.That(store.Notes["organize"].Version == 9 && store.Activity.Values.Any(x => x.Status == "failed"), "Organization conflict was not retained and visible");
        model.BeforeFinish = null;

        store.Add("late");
        store.Chunks["late"].Add(new ChunkRecord { Id = "awaiting", Status = ChunkStatus.PendingAsr });
        store.Gates["late"] = new NoteGateRecord { SessionId = "late", FlushRequested = true, FlushVersion = 1 };
        await Tick("late");
        Expect.That(store.Gates["late"].FlushRequested && gate.Calls == 0, "Stop acknowledged audio before its confirmed ASR arrived");
        store.Chunks["late"][0].Status = ChunkStatus.Transcribed;
        store.Speech("late", "late-final", "This complete example arrived after Stop.");
        gate.Allow = false;
        await Tick("late");
        Expect.That(store.Notes.ContainsKey("late") && !store.Gates["late"].FlushRequested, "Stop failed to include a late final source");
        var count = gate.Calls;
        await Tick("late");
        Expect.That(gate.Calls == count, "Completed Stop input was evaluated again");

        store.Add("deferred");
        store.Speech("deferred", "half", "The unresolved sentence ends with");
        store.Gates["deferred"] = new NoteGateRecord { SessionId = "deferred", FlushRequested = true, FlushVersion = 1 };
        model.DeferAll = true;
        await Tick("deferred");
        count = gate.Calls;
        await Tick("deferred");
        Expect.That(!store.Notes.ContainsKey("deferred") && store.Transcripts["deferred"][0].NoteStatus == "deferred" && gate.Calls == count,
            "All-deferred output saved an empty version, completed the source or paid for unchanged Stop input again");
        model.DeferAll = false;
        gate.Allow = true;

        store.Add("mixed-deferral");
        store.Speech("mixed-deferral", "written", "A complete source-backed point.");
        store.Speech("mixed-deferral", "continuation", "The next formula begins with");
        model.DeferLast = true;
        await agent.Generate("mixed-deferral", CancellationToken.None);
        Expect.That(store.Transcripts["mixed-deferral"][0].NoteStatus == "completed" && store.Transcripts["mixed-deferral"][1].NoteStatus == "deferred" &&
            store.Notes["mixed-deferral"].Coverage.Single(x => x.SourceId == "continuation").Status == "deferred",
            "Mixed written/deferred output left the incomplete source pending or falsely completed it");
        model.DeferLast = false;

        store.Add("render-citations");
        store.Speech("render-citations", "cited-source", "An exact supported definition.");
        model.OmitInlineCitations = true;
        await agent.Generate("render-citations", CancellationToken.None);
        model.OmitInlineCitations = false;
        Expect.That(store.Transcripts["render-citations"][0].NoteStatus == "completed" &&
            store.Notes["render-citations"].Citations.Single().SourceIds.SequenceEqual(["cited-source"]) &&
            store.Notes["render-citations"].Sections.Single().Markdown.Contains(store.Notes["render-citations"].Sections.Single().Points.Single().Text),
            "Canonical sourceIds were not rendered as durable citations in the actual point body");

        store.Add("backlog");
        for (var i = 0; i < 40; i++)
        {
            store.Speech("backlog", "fragment-" + i, "Unresolved fragment " + i);
            store.Transcripts["backlog"][i].NoteStatus = "deferred";
        }
        store.Speech("backlog", "new-topic", "A complete new topic beyond the old unresolved window.");
        await Tick("backlog");
        Expect.That(store.Transcripts["backlog"].Last().NoteStatus == "completed" && store.Transcripts["backlog"].Take(32).All(x => x.NoteStatus == "deferred"),
            "An old deferred window blocked meaningful later input or falsely completed unseen sources");

        store.Add("material");
        store.Materials["material"].Add(new Material { Id = new string('c', 32), Name = "Chinese passage", Text = new string('字', 7000) });
        await Tick("material");
        Expect.That(store.Notes["material"].Coverage.Count > 0 && gate.Inputs.Any(x => x.Contains("Chinese passage")) && model.InputLengths.Max() <= 48000,
            "Material-only / escaped Chinese input was lost or exceeded the resource limit");

        store.Add("oversized");
        store.Speech("oversized", "huge", new string('字', 10000));
        count = gate.Calls;
        await Tick("oversized");
        Expect.That(gate.Calls == count && !store.Notes.ContainsKey("oversized") &&
            store.Activity.Values.Any(x => x.SessionId == "oversized" && x.Status == "failed"),
            "One unbounded ASR passage was truncated, sent to a provider or failed invisibly");

        Console.WriteLine("Note generation checks passed: protected section organized alone, stale/concurrent bases rejected, Stop awaits late speech, " +
            "deferrals keep sources open, citations rendered, material-only and oversized input bounded, live draft readable");
    }

    // The activity popover polls a compact record and shows the Markdown inside the streamed JSON.
    static void LiveDraft()
    {
        var execution = new ActivityRecord
        {
            Id = "wire-check",
            Task = "Note revision",
            PromptText = new string('x', 100000),
            Draft = "Readable live draft",
            PromptHash = "exact-instructions-hash",
            SourceIds = ["source-check"]
        };
        var compactActivity = JsonSerializer.Serialize(new[] { execution }, AiActivity.NotesResponseOptions);
        Expect.That(compactActivity.Length < 2000 && !compactActivity.Contains("promptText") && compactActivity.Contains("Readable live draft") &&
            compactActivity.Contains("exact-instructions-hash") && JsonSerializer.Serialize(execution, JsonOptions).Contains("promptText") &&
            execution.PromptText.Length == 100000, "Notes polling lost required metadata or discarded the saved/group prompt");
        Expect.That(NoteAgent.ReadableDraft("{\"sections\":[{\"markdown\":\"## Topic\\n\\nReadable partial") == "## Topic\n\nReadable partial",
            "Structured stream exposed raw JSON instead of a readable Markdown draft");
        Expect.That(NoteAgent.ReadableDraft("{\"sections\":[{\"points\":[{\"text\":\"First point [T001]\"},{\"text\":\"Second point") ==
            "First point [T001]\n\nSecond point", "Canonical point stream lost readable draft content");
    }
}
