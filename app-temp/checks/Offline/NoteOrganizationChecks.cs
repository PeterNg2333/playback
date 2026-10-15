using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Application.Notes;
using Playback.Domain;
using Playback.Domain.Notes;
using System.Text.Json;

static class NoteOrganizationChecks
{
    public static async Task Run()
    {
        await TriggersAndRestart(); await DirtyAgeAndDisable(); await OutlineAndPriority(); await Rewrites();
        Console.WriteLine("Organization checks passed: small-note revision/age triggers, meaningful-only counts, cooldown, restart acknowledgement, inherited disable, outline without lecture prose, generation priority, strict rewrite previews/retention, semantic opt-in, stale rejection and retry reuse");
    }
    static NoteAgent Agent(MemoryStore store, OrganizationModel model, TestClock clock) => new(store, model, new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, new AiActivity(store), clock);
    static async Task Configure(MemoryStore store, string id, ProcessingOverrides overrides)
    {
        var settings = await store.SessionProcessingSettings(id); await store.SaveSessionProcessingSettings(id, new(settings.Revision, overrides, settings.InheritedRevision));
    }
    static async Task Tick(NoteAgent agent, TestClock clock, double seconds, string id)
    {
        clock.Advance(seconds); agent.Tick([id], default); await agent.Drain().WaitAsync(TimeSpan.FromSeconds(5));
    }
    static async Task TriggersAndRestart()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; store.Add("revisions"); var model = new OrganizationModel();
        await Configure(store, "revisions", new(CheckIntervalSeconds: 2, OrganizationChangedRevisions: 3, OrganizationCooldownSeconds: 15, OrganizationMaxDirtyAgeSeconds: 30));
        await using (var agent = Agent(store, model, clock))
        {
            for (var i = 0; i < 3; i++) {store.Speech("revisions", "s" + i, "Small cited fact " + i); await agent.Generate("revisions", default);}
            var note = store.Notes["revisions"]; Expect.That(note.MeaningfulRevision == 3, "Meaningful content revisions were not counted");
            var adapter = new NoteStoreAdapter(store); await new NoteCommands(adapter, adapter).SaveNote("revisions", note.Markdown, "user", 3);
            Expect.That(store.Notes["revisions"].MeaningfulRevision == 3, "No-op editor save dirtied notes");
            await Tick(agent, clock, 2, "revisions");
            Expect.That(model.Organizations == 1 && store.Organizations["revisions"].DueReason == "changed-revisions" && store.Notes["revisions"].Markdown.Contains("Definitions and examples"), "Small notes did not receive structural organization");
            await Tick(agent, clock, 2, "revisions"); Expect.That(model.Organizations == 1, "Idle organization repeated a provider call");
            store.Speech("revisions", "new", "Another small cited fact"); await agent.Generate("revisions", default);
            await Configure(store, "revisions", new(CheckIntervalSeconds: 2, OrganizationChangedRevisions: 1, OrganizationCooldownSeconds: 15, OrganizationMaxDirtyAgeSeconds: 30));
            await Tick(agent, clock, 2, "revisions"); Expect.That(model.Organizations == 1, "Cooldown was defeated by a policy change");
            await Tick(agent, clock, 15, "revisions"); Expect.That(model.Organizations == 2, "Changed notes did not organize after cooldown");
        }
        var saved = store.Notes["revisions"]; var state = store.Organizations["revisions"]; state.Status = "organizing"; state.OrganizedMeaningfulRevision = 0; state.InputHash = saved.InputHash!;
        await using (var restarted = Agent(store, model, clock))
        {
            await Tick(restarted, clock, 2, "revisions");
            Expect.That(model.Organizations == 2 && store.Organizations["revisions"].OrganizedMeaningfulRevision == saved.MeaningfulRevision, "Restart repeated committed organization before recovering its acknowledgement");
        }
    }
    static async Task DirtyAgeAndDisable()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; store.Add("age"); store.Speech("age", "small", "A complete small definition."); var model = new OrganizationModel();
        await Configure(store, "age", new(CheckIntervalSeconds: 2, OrganizationMaxDirtyAgeSeconds: 30, OrganizationCooldownSeconds: 15));
        await using var agent = Agent(store, model, clock); await agent.Generate("age", default);
        await Tick(agent, clock, 29, "age"); Expect.That(model.Organizations == 0, "Dirty age admitted too early");
        await Tick(agent, clock, 2, "age"); Expect.That(model.Organizations == 1 && store.Organizations["age"].DueReason == "dirty-age", "Small notes missed the dirty-age deadline");
        store.Add("disabled"); store.Speech("disabled", "off", "Explicit disable is retained.");
        await store.SaveWorkspaceProcessingSettings(new(0, new(OrganizationEnabled: false)));
        await agent.Generate("disabled", default); await Tick(agent, clock, 120, "disabled");
        Expect.That(model.Organizations == 1 && !(await store.SessionProcessingSettings("disabled")).Effective.OrganizationEnabled, "Inherited false was ignored");
        foreach (var invalid in new[] {new ProcessingPolicy(OrganizationChangedRevisions: 0), new(OrganizationMaxDirtyAgeSeconds: 29), new(OrganizationCooldownSeconds: 301), new(OrganizationMaxDirtyAgeSeconds: 30, OrganizationCooldownSeconds: 60), new(OrganizationScope: "all-prose")})
        {
            try {invalid.Validate(); throw new CheckFailed("Invalid organization policy accepted");} catch (ProcessingPolicyValidationException) { }
        }
    }
    static async Task OutlineAndPriority()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; store.Add("outline");
        var body = new string('中', 180000) + " [long-source]";
        var root = NoteTree.Canonical(new() {Id = "lecture", Title = "Legacy lecture", Markdown = "## Legacy lecture\n\n" + body, Points = [new() {Id = "long", Text = body, SourceIds = ["long-source"]}]});
        var second = NoteTree.Canonical(new() {Id = "other", Title = "Related topic", Order = 1, Markdown = "## Related topic\n\nA smaller body [other-source]", Points = [new() {Id = "other-point", Text = "A smaller body [other-source]", SourceIds = ["other-source"]}]});
        store.Notes["outline"] = new() {SessionId = "outline", Version = 1, SchemaVersion = 2, Sections = [root, second], Markdown = NoteTree.Render([root, second], true), MeaningfulRevision = 1, RecentChanges = [new() {Revision = 1, At = clock.GetUtcNow().UtcDateTime, TopicIds = [root.Id, second.Id]}]};
        store.Speech("outline", "long-source", "Confirmed original."); store.Speech("outline", "other-source", "Related original."); foreach (var source in store.Transcripts["outline"]) source.NoteStatus = "completed";
        await Configure(store, "outline", new(CheckIntervalSeconds: 2, OrganizationChangedRevisions: 1, OrganizationScope: "document-outline"));
        var model = new OrganizationModel {BlockOrganization = true}; await using var agent = Agent(store, model, clock);
        clock.Advance(2); agent.Tick(["outline"], default); await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.Add("generation"); store.Speech("generation", "fresh", "New speech gets generation capacity.");
        await agent.Generate("generation", default).WaitAsync(TimeSpan.FromSeconds(5));
        Expect.That(store.Notes["generation"].Version == 1 && model.LastOrganizationInput.Length < 2000 && !model.LastOrganizationInput.Contains(body), "Organization blocked generation or sent the full lecture prose");
        model.Release.TrySetResult(); await agent.Drain().WaitAsync(TimeSpan.FromSeconds(5));
        var organized = store.Notes["outline"];
        Expect.That(organized.Sections.Single(x => x.Id == "other").ParentId == "lecture" && NoteSections.Expand(organized.Sections.Single(x => x.Id == "lecture").Blocks.Single().Body, organized.Citations) == body, "Outline organization rewrote/lost a large legacy body");
    }
    static async Task Rewrites()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; store.Add("rewrite"); store.Speech("rewrite", "source", "Only under one shared bottleneck, n=10 yields $R/10$.\n\nExample: keep the uncertainty; the ASR is unclear.\n\n```c\nint n = 10;\n```\n\n假設只有一個瓶頸。");
        var model = new OrganizationModel {Shorten = true}; await using var agent = Agent(store, model, clock);
        await agent.Generate("rewrite", default); var before = JsonSerializer.Serialize(store.Notes["rewrite"]);
        await Configure(store, "rewrite", new(CheckIntervalSeconds: 2, OrganizationChangedRevisions: 1));
        await Tick(agent, clock, 2, "rewrite");
        Expect.That(store.RewriteProposals.Count == 1 && !store.RewriteProposals.Values.Single().ReadyForAcceptance && JsonSerializer.Serialize(store.Notes["rewrite"]) == before, "Failed rewrite lost/moved original prose or lacked a preview");
        await Tick(agent, clock, 120, "rewrite"); Expect.That(model.Organizations == 1, "Review-required rewrite repeated paid automatic work");
        var adapter = new NoteStoreAdapter(store); var commands = new NoteCommands(adapter, adapter, adapter); var note = store.Notes["rewrite"]; var topic = note.Sections.Single(); var block = topic.Blocks.Single();
        var preview = NoteRewrite.Preview(note, topic.Id, [new() {TargetBlockId = block.Id, RetainedBlockIds = [block.Id], Body = "Reviewed introduction.\n\n" + block.Body, Role = "definition"}], ["source"], clock.GetUtcNow().UtcDateTime);
        Expect.That(preview.ReadyForAcceptance, "Exact retained math/code/conditions/uncertainty/example failed mechanical checks");
        await store.SaveNoteRewrite(preview);
        await Expect.RejectsAsync(() => commands.AcceptRewrite("rewrite", preview.Id, false), "Rewrite accepted without semantic review opt-in");
        await commands.AcceptRewrite("rewrite", preview.Id, true); var accepted = store.Notes["rewrite"];
        Expect.That(accepted.Version == 2 && accepted.Sections.Single().Blocks.Single().Id == block.Id && accepted.Sections.Single().Blocks.Single().UserEdited && accepted.Markdown.Contains("假設"), "Accepted preview lost stable identity/protection/body");
        await commands.AcceptRewrite("rewrite", preview.Id, true); Expect.That(store.Notes["rewrite"].Version == 2, "Retry duplicated an accepted rewrite version");
        await Expect.RejectsAsync(() => commands.AcceptRewrite("rewrite", store.RewriteProposals.Values.Single(x => !x.ReadyForAcceptance).Id, true), "Stale/blocked preview overwrote accepted notes");
    }
    sealed class OrganizationModel : GeminiLanguageModel
    {
        readonly FixtureNoteModel writer = new();
        public override bool IsConfigured => true;
        public int Organizations; public bool BlockOrganization, Shorten;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string LastOrganizationInput = "";
        public override async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            if (name != "LectureSectionOrganizer") return await writer.Generate(name, instructions, prompt, ct, model, onUpdate, onUsage);
            Organizations++; LastOrganizationInput = prompt; Started.TrySetResult(); if (BlockOrganization) await Release.Task.WaitAsync(ct);
            using var doc = JsonDocument.Parse(prompt); var root = doc.RootElement; NotePatch patch;
            if (root.GetProperty("trigger").GetString() == "document-outline")
                patch = new() {Operations = [new() {Kind = "move-topic", TopicId = root.GetProperty("topicOutline")[1][0].GetString()!, ParentId = root.GetProperty("topicOutline")[0][0].GetString()}]};
            else
            {
                var topic = root.GetProperty("editableSections")[0];
                if (Shorten) patch = new() {Sections = [new() {Id = topic.GetProperty("id").GetString()!, Title = "Unsafe summary", Points = [new() {Text = "Flows share capacity.", SourceIds = topic.GetProperty("points").EnumerateArray().SelectMany(x => x.GetProperty("sourceIds").EnumerateArray()).Select(x => x.GetString()!).Distinct().ToList(), Retains = topic.GetProperty("points").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList()}]}]};
                else patch = new() {Operations = [new() {Kind = "create-topic", TopicId = "new:details", ParentId = topic.GetProperty("id").GetString(), Title = "Definitions and examples"},
                    new() {Kind = "move-block", TopicId = "new:details", BlockId = topic.GetProperty("blocks")[0].GetProperty("id").GetString()!}]};
            }
            return JsonSerializer.Serialize(patch);
        }
    }
}
