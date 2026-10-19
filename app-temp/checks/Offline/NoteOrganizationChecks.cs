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
        await TriggersAndRestart(); await SavedRunMoves(); await DirtyAgeAndDisable(); await OutlineAndPriority(); await Rewrites();
        Console.WriteLine("Organization checks passed: structure triggers (large topic, crowded level) not time, unchanged structure not re-asked, restart acknowledgement, block moves name their destination (saved Qwen split replayed), inherited disable, outline without lecture prose, generation priority, strict rewrite previews/retention, semantic opt-in, stale rejection and retry reuse");
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
    // Seeds saved notes whose topics hold the given number of cited blocks; every source is already written.
    static void Seed(MemoryStore store, TestClock clock, string id, params (string Id, string? Parent, int Blocks)[] topics)
    {
        store.Add(id);
        var sections = topics.Select((topic, i) => new NoteSection { Id = topic.Id, Title = "Topic " + topic.Id, ParentId = topic.Parent, Order = i, SchemaVersion = 2,
            Blocks = Enumerable.Range(0, topic.Blocks).Select(b =>
            {
                var source = topic.Id + "-s" + b; var body = $"Fact {b} about {topic.Id}. [{source}]";
                store.Speech(id, source, $"Fact {b} about {topic.Id}."); store.Transcripts[id].Last().NoteStatus = "completed";
                return new NoteBlock { Id = topic.Id + "-b" + b, Order = b, Role = "detail", Body = body, CitationIds = [source],
                    Anchors = [new() { Id = topic.Id + "-p" + b, Length = body.Length, SourceIds = [source] }] };
            }).ToList() }).ToList();
        store.Notes[id] = new() { SessionId = id, Version = 1, SchemaVersion = 2, Sections = sections, Markdown = NoteTree.Render(sections, true), MeaningfulRevision = 1,
            RecentChanges = [new() { Revision = 1, At = clock.GetUtcNow().UtcDateTime, TopicIds = sections.Select(x => x.Id).ToList() }] };
    }
    // Organization follows structure, not time: small notes are left alone however long they wait; a topic over
    // six blocks gets subtopics; an unchanged structure is not asked again; a level over six topics is regrouped
    // from the outline; a restart recovers a committed organization without repeating it.
    static async Task TriggersAndRestart()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; var model = new OrganizationModel();
        Seed(store, clock, "small", ("a", null, 3), ("b", null, 2));
        await Configure(store, "small", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        Seed(store, clock, "large", ("big", null, 7));
        await Configure(store, "large", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        Seed(store, clock, "crowded", Enumerable.Range(0, 7).Select(i => ("t" + i, (string?)null, 1)).ToArray());
        await Configure(store, "crowded", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        await using (var agent = Agent(store, model, clock))
        {
            await Tick(agent, clock, 600, "small"); Expect.That(model.Organizations == 0, "Organization ran on elapsed time alone");
            await Tick(agent, clock, 2, "large");
            Expect.That(model.Organizations == 1 && store.Organizations["large"].DueReason == "large-topic" &&
                store.Notes["large"].Sections.Any(x => x.ParentId == "big" && x.Title == "Definitions and examples" && x.Blocks.Count == 1) &&
                store.Activity.Values.Any(x => x.SessionId == "large" && (x.Summary ?? "").Contains("1 operations outside the selected scope dropped")),
                "A topic over six blocks did not get subtopics from an outline key, or an out-of-scope operation failed the regrouping");
            await Tick(agent, clock, 30, "large"); Expect.That(model.Organizations == 1, "An unchanged structure repeated a provider call");
            await Tick(agent, clock, 2, "crowded");
            Expect.That(model.Organizations == 2 && store.Organizations["crowded"].DueReason == "crowded-level" &&
                model.LastOrganizationInput.Contains("document-outline") && store.Notes["crowded"].Sections.Count(x => x.ParentId is null) == 6,
                "A level over six topics was not regrouped from the outline");
            // The same regrouping is dropped when the two topics were spoken far apart in the lecture.
            Seed(store, clock, "distant", Enumerable.Range(0, 7).Select(i => ("t" + i, (string?)null, 1)).ToArray());
            store.Transcripts["distant"].Single(x => x.Id == "t1-s0").StartMs = 20 * 60_000;
            await Configure(store, "distant", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
            await Tick(agent, clock, 2, "distant");
            Expect.That(model.Organizations == 3 && store.Notes["distant"].Sections.All(x => x.ParentId is null) &&
                store.Activity.Values.Any(x => x.SessionId == "distant" && (x.Summary ?? "").Contains("moves under distant topics dropped")),
                "The organizer grouped topics spoken far apart");
        }
        var saved = store.Notes["large"]; var state = store.Organizations["large"]; state.Status = "organizing"; state.OrganizedMeaningfulRevision = 0; state.InputHash = saved.InputHash!;
        await using (var restarted = Agent(store, model, clock))
        {
            await Tick(restarted, clock, 2, "large");
            Expect.That(model.Organizations == 3 && store.Organizations["large"].OrganizedMeaningfulRevision == saved.MeaningfulRevision, "Restart repeated committed organization before recovering its acknowledgement");
        }
    }
    // The saved Qwen split (SavedOrganizerReplies) named an in-scope block in every move but no destination, so all
    // seven moves were dropped "outside the selected scope" and the split did nothing. Moves are now placements with a
    // required destination that the schema limits to the selected topics and the reply's new keys: the same decisions
    // apply in full, and a reply without destinations is reported as such, not as reaching outside the scope.
    static async Task SavedRunMoves()
    {
        const string id = "saved-organizer";
        async Task<(MemoryStore Store, OrganizationModel Model)> Organize(string reply)
        {
            var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; store.Add(id);
            NoteSection Topic(string topicId, string title, string? parent, (string Id, string Body)[] blocks) => new() { Id = topicId, Title = title, ParentId = parent, SchemaVersion = 2,
                Blocks = blocks.Select((block, i) =>
                {
                    var source = block.Id + "-source"; var body = block.Body + " [" + source + "]";
                    store.Speech(id, source, block.Body); store.Transcripts[id].Last().NoteStatus = "completed";
                    return new NoteBlock { Id = block.Id, Order = i, Role = "detail", Body = body, CitationIds = [source],
                        Anchors = [new() { Id = block.Id + "-anchor", Length = body.Length, SourceIds = [source] }] };
                }).ToList() };
            List<NoteSection> sections = [Topic(SavedOrganizerReplies.ParentId, SavedOrganizerReplies.ParentTitle, null, [("parent-block", "系統呼叫由使用者模式進入核心模式。")]),
                Topic(SavedOrganizerReplies.TopicId, SavedOrganizerReplies.TopicTitle, SavedOrganizerReplies.ParentId, SavedOrganizerReplies.Blocks)];
            store.Notes[id] = new() { SessionId = id, Version = 1, SchemaVersion = 2, Sections = sections, Markdown = NoteTree.Render(sections, true), MeaningfulRevision = 1,
                RecentChanges = [new() { Revision = 1, At = clock.GetUtcNow().UtcDateTime, TopicIds = [SavedOrganizerReplies.TopicId] }] };
            await Configure(store, id, new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
            var model = new OrganizationModel { Reply = reply };
            await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, id);
            Expect.That(model.Organizations == 1 && store.Organizations[id].DueReason == "large-topic", "The saved topic of seven blocks was not organized as a large topic");
            return (store, model);
        }
        static string Summary(MemoryStore store) => store.Activity.Values.Single(x => x.Task == "Section organization").Summary ?? "";

        var (saved, _) = await Organize(SavedOrganizerReplies.SavedReply);
        Expect.That(Summary(saved).Contains("7 moves without a destination dropped") && !Summary(saved).Contains("outside the selected scope"),
            "The saved reply's moves were blamed on the scope instead of their missing destination: " + Summary(saved));

        var (placed, model) = await Organize(SavedOrganizerReplies.PlacedReply);
        var topics = placed.Notes[id].Sections;
        // In reading order: moved blocks keep the order the reply listed them in.
        List<string> Blocks(string title) => topics.SingleOrDefault(x => x.Title == title && x.ParentId == SavedOrganizerReplies.TopicId)?.Blocks.OrderBy(x => x.Order).Select(x => x.Id).ToList() ?? [];
        Expect.That(Blocks("調試器操作與符號解析").SequenceEqual(["point_c7fdf71b9556791173b0", "point_5821ed05a6946b1e8311"]) &&
            Blocks("用戶內核數據地址計算").SequenceEqual(["point_6a295a4e41d08f6fe774", "point_97dd9b7e49411908801c", "point_157cb24450328a5be7d8"]) &&
            topics.Single(x => x.Id == SavedOrganizerReplies.TopicId).Blocks.Count == 2 && !Summary(placed).Contains("dropped"),
            "The organizer's placements did not move each block into the subtopic it named: " + Summary(placed));

        // The schema offers each move a destination only among the selected topic and the new keys, never "" or the
        // parent outside the scope; a block moves only this way, not through a move-block operation.
        var schema = ((Microsoft.Extensions.AI.ChatResponseFormatJson)ModelOutputContract.ResponseFormat("LectureSectionOrganizer", model.LastOrganizationInput)!).Schema!.Value.GetProperty("properties");
        static List<string> Enum(JsonElement property) => property.TryGetProperty("enum", out var values) ? values.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        var move = schema.TryGetProperty("moves", out var moves) ? moves.GetProperty("items") : default;
        Expect.That(move.ValueKind == JsonValueKind.Object &&
            Enum(move.GetProperty("properties").GetProperty("to")).SequenceEqual([SavedOrganizerReplies.TopicId, .. NoteWorkflow.NewTopicKeys]) &&
            Enum(move.GetProperty("properties").GetProperty("block")).Order().SequenceEqual(SavedOrganizerReplies.Blocks.Select(x => x.Id).Order()) &&
            move.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["block", "to"]) &&
            !Enum(schema.GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("kind")).Contains("move-block"),
            "The organizer schema let a move leave out its destination or name one outside the selected scope");
    }
    static async Task DirtyAgeAndDisable()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; var model = new OrganizationModel();
        Seed(store, clock, "disabled", ("big", null, 7));
        await store.SaveWorkspaceProcessingSettings(new(0, new(OrganizationEnabled: false)));
        await using var agent = Agent(store, model, clock); await Tick(agent, clock, 120, "disabled");
        Expect.That(model.Organizations == 0 && !(await store.SessionProcessingSettings("disabled")).Effective.OrganizationEnabled, "Inherited false was ignored");
        foreach (var invalid in new[] {new ProcessingPolicy(OrganizationChangedRevisions: 0), new(OrganizationMaxDirtyAgeSeconds: 29), new(OrganizationCooldownSeconds: 301), new(OrganizationScope: "all-prose")})
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
        // Five more small root topics make the level crowded, which regroups from the outline.
        var extra = Enumerable.Range(0, 5).Select(i => NoteTree.Canonical(new() {Id = "extra" + i, Title = "Extra " + i, Order = 2 + i, Markdown = $"## Extra {i}\n\nSmall fact [extra-source{i}]",
            Points = [new() {Id = "extra-point" + i, Text = $"Small fact [extra-source{i}]", SourceIds = ["extra-source" + i]}]})).ToList();
        List<NoteSection> all = [root, second, .. extra];
        store.Notes["outline"] = new() {SessionId = "outline", Version = 1, SchemaVersion = 2, Sections = all, Markdown = NoteTree.Render(all, true), MeaningfulRevision = 1, RecentChanges = [new() {Revision = 1, At = clock.GetUtcNow().UtcDateTime, TopicIds = all.Select(x => x.Id).ToList()}]};
        store.Speech("outline", "long-source", "Confirmed original."); store.Speech("outline", "other-source", "Related original.");
        for (var i = 0; i < 5; i++) store.Speech("outline", "extra-source" + i, "Extra original " + i);
        foreach (var source in store.Transcripts["outline"]) source.NoteStatus = "completed";
        await Configure(store, "outline", new(CheckIntervalSeconds: 2));
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
        // A small note is not organized automatically; the owner asks for this organization explicitly.
        var rewriteTopic = store.Notes["rewrite"].Sections.Single();
        await agent.Organize("rewrite", rewriteTopic.Id, store.Notes["rewrite"].Version, default);
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
        public string? Reply;
        public override async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            if (name != "LectureSectionOrganizer") return await writer.Generate(name, instructions, prompt, ct, model, onUpdate, onUsage);
            Organizations++; LastOrganizationInput = prompt; Started.TrySetResult(); if (BlockOrganization) await Release.Task.WaitAsync(ct);
            if (Reply is not null) return Reply;
            using var doc = JsonDocument.Parse(prompt); var root = doc.RootElement; NotePatch patch;
            if (root.GetProperty("trigger").GetString() == "document-outline")
                patch = new() {Operations = [new() {Kind = "move-topic", TopicId = root.GetProperty("topicOutline")[1][0].GetString()!, ParentId = root.GetProperty("topicOutline")[0][0].GetString()}]};
            else
            {
                var topic = root.GetProperty("editableSections")[0];
                if (Shorten) patch = new() {Sections = [new() {Id = topic.GetProperty("id").GetString()!, Title = "Unsafe summary", Points = [new() {Text = "Flows share capacity.", SourceIds = topic.GetProperty("points").EnumerateArray().SelectMany(x => x.GetProperty("sourceIds").EnumerateArray()).Select(x => x.GetString()!).Distinct().ToList(), Retains = topic.GetProperty("points").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList()}]}]};
                // The reply shape the schema asks for: a new topic declared in outline, a move naming its key, and
                // one rename outside the selected scope and one without a title, both dropped instead of failing the regrouping.
                else patch = new() {Outline = [new() {Key = "new1", Title = "Definitions and examples", Parent = topic.GetProperty("id").GetString()!}],
                    Moves = [new() {Block = topic.GetProperty("blocks")[0].GetProperty("id").GetString()!, To = "new1"}],
                    Operations = [new() {Kind = "rename-topic", TopicId = "elsewhere", Title = "Not in scope"}, new() {Kind = "rename-topic", TopicId = "new1"}]};
            }
            return JsonSerializer.Serialize(patch);
        }
    }
}
