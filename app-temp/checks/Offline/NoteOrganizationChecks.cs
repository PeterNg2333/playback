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
        await TriggersAndRestart(); await SavedRunMoves(); await SavedRunRegroup(); await RegroupDepth(); await CrowdedSubtopicsReview(); await LargeThirdLevelTopic(); await FullTopicReview(); await DirtyAgeAndDisable(); await OutlineAndPriority(); await Rewrites();
        Console.WriteLine("Organization checks passed: structure triggers (large topic, crowded level) not time, unchanged structure not re-asked, restart acknowledgement, block and topic moves name their destination (saved Qwen split and regroup replayed), only groupable levels regrouped and too-deep moves dropped, over six third-level subtopics reviewed (merge keeping every block and citation, or left as they are), inherited disable, outline without lecture prose, generation priority, strict rewrite previews/retention, semantic opt-in, stale rejection and retry reuse");
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
    // The first v14 live run regrouped a crowded level seven times without effect: every move-topic left out its
    // parentId, so each topic "moved" to the top level where it already was. Topics now move as placements too,
    // with a required destination; a move that leaves a topic where it is gets counted, and an outline regroup may
    // rename (two calls in that run failed on a rename).
    static async Task SavedRunRegroup()
    {
        const string id = "saved-regroup";
        async Task<(MemoryStore Store, OrganizationModel Model)> Regroup(string reply)
        {
            var clock = new TestClock(); var store = new MemoryStore {Clock = clock};
            Seed(store, clock, id, Enumerable.Range(0, 7).Select(i => ("t" + i, (string?)null, 1)).ToArray());
            await Configure(store, id, new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
            var model = new OrganizationModel { Reply = reply };
            await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, id);
            Expect.That(model.Organizations == 1 && store.Organizations[id].DueReason == "crowded-level", "Seven top-level topics were not regrouped from the outline");
            return (store, model);
        }
        static ActivityRecord Call(MemoryStore store) => store.Activity.Values.Single(x => x.Task == "Section organization");

        var (saved, _) = await Regroup(SavedOrganizerReplies.SavedRegroupReply);
        Expect.That((Call(saved).Summary ?? "").Contains("7 moves that leave a block or topic where it is ignored") && saved.Notes[id].Sections.All(x => x.ParentId is null),
            "A regroup whose topic moves named no parent was not reported: " + Call(saved).Summary);

        var (placed, model) = await Regroup(SavedOrganizerReplies.PlacedRegroupReply);
        var topics = placed.Notes[id].Sections;
        string? Parent(string topic) => topics.Single(x => x.Id == topic).ParentId is { } parent ? topics.Single(x => x.Id == parent).Title : null;
        Expect.That(Call(placed).Status == "completed" && topics.Count(x => x.ParentId is null) == 2 &&
            new[] { "t0", "t1", "t2" }.All(x => Parent(x) == "AI 輔助教學與簡報製作") && new[] { "t3", "t4", "t5", "t6" }.All(x => Parent(x) == "Kernel 調試與地址查詢") &&
            topics.Single(x => x.Id == "t6").Title == "使用者共享資料",
            "An outline regroup did not place each topic under the parent it named, or refused a rename: " + Call(placed).Status + " " + Call(placed).Summary);

        var schema = ((Microsoft.Extensions.AI.ChatResponseFormatJson)ModelOutputContract.ResponseFormat("LectureSectionOrganizer", model.LastOrganizationInput)!).Schema!.Value.GetProperty("properties");
        static List<string> Enum(JsonElement property) => property.TryGetProperty("enum", out var values) ? values.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        var move = schema.TryGetProperty("topicMoves", out var moves) ? moves.GetProperty("items") : default;
        var aliases = Enumerable.Range(0, 7).Select(i => "S" + i.ToString("D3")).ToList();
        var kinds = Enum(schema.GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("kind"));
        Expect.That(move.ValueKind == JsonValueKind.Object && Enum(move.GetProperty("properties").GetProperty("to")).SequenceEqual([.. aliases, .. NoteWorkflow.NewTopicKeys]) &&
            Enum(move.GetProperty("properties").GetProperty("topic")).SequenceEqual(aliases) &&
            move.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["topic", "to"]) &&
            kinds.SequenceEqual(["rename-topic", "reorder-topic"]),
            "The outline regroup schema let a topic move leave out its parent, or offered operations an outline cannot apply");
    }
    // The v15 live run (2026-10-19-p3-organize-v15) kept asking to regroup seven subtopics already at the third level,
    // which no new parent can hold; the model regrouped the top level instead, and two replies failed outright
    // because one move would have nested a topic four levels deep. A crowded level counts only when it can be
    // grouped, the input names the crowded level and how many levels lie below each topic, and a move that would
    // nest deeper than three levels is dropped while the rest of the regroup applies.
    static async Task RegroupDepth()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock};
        Seed(store, clock, "deep", [("top", null, 1), ("chain", "top", 1), .. Enumerable.Range(0, 7).Select(i => ("leaf" + i, (string?)"chain", 1)), ("other", null, 1)]);
        await Configure(store, "deep", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        var model = new OrganizationModel { Reply = "{\"outline\":[],\"moves\":[],\"topicMoves\":[],\"operations\":[],\"deferred\":[]}" };
        await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, "deep");
        Expect.That(model.Organizations == 1 && store.Organizations["deep"].DueReason == "crowded-subtopics" && !model.LastOrganizationInput.Contains("document-outline"),
            "Seven subtopics at the third level, which no new parent can hold, were regrouped from the outline instead of reviewed with their blocks");

        Seed(store, clock, "wide", [.. Enumerable.Range(0, 7).Select(i => ("t" + i, (string?)null, 1)), ("t0-sub", "t0", 1), ("t0-leaf", "t0-sub", 1)]);
        await Configure(store, "wide", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        model = new OrganizationModel { ReplyFor = input =>
        {
            var rows = input.GetProperty("topicOutline").EnumerateArray().ToDictionary(x => x[1].GetString()!, x => x[0].GetString()!);
            return JsonSerializer.Serialize(new NotePatch { Outline = [new() { Key = "new1", Title = "Grouped", Parent = "" }],
                TopicMoves = [new() { Topic = rows["Topic t0"], To = "new1" }, new() { Topic = rows["Topic t1"], To = "new1" }, new() { Topic = rows["Topic t2"], To = "new1" }] });
        } };
        await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, "wide");
        var input = JsonDocument.Parse(model.LastOrganizationInput).RootElement;
        var columns = input.GetProperty("topicColumns").EnumerateArray().Select(x => x.GetString()).ToList();
        var below = columns.IndexOf("levelsBelow");
        var t0Row = input.GetProperty("topicOutline").EnumerateArray().FirstOrDefault(x => x[1].GetString() == "Topic t0");
        Expect.That(input.TryGetProperty("crowdedLevels", out var crowded) && crowded.EnumerateArray().Select(x => x.GetString()).SequenceEqual([""]) &&
            below >= 0 && t0Row.ValueKind == JsonValueKind.Array && t0Row[below].GetInt32() == 2,
            "The outline regroup input did not name the crowded level or the levels below each topic");
        var call = store.Activity.Values.Single(x => x.SessionId == "wide" && x.Task == "Section organization");
        var topics = store.Notes["wide"].Sections;
        var grouped = topics.SingleOrDefault(x => x.Title == "Grouped");
        Expect.That(call.Status == "completed" && (call.Summary ?? "").Contains("1 moves that would nest deeper than three levels dropped") &&
            grouped is not null && topics.Where(x => x.ParentId == grouped.Id).Select(x => x.Id).Order().SequenceEqual(["t1", "t2"]) && topics.Single(x => x.Id == "t0").ParentId is null,
            "A move that would nest a topic four levels deep failed the whole regroup: " + call.Status + " " + call.Summary);
    }
    // Six subtopics is a review threshold, not a limit. More than six at the third level, where no new parent fits,
    // gets one organizer review of the parent with its subtopics and their blocks: the organizer may merge subtopics
    // (moving every block of one into another; the emptied one is removed), rename or reorder them, or leave them.
    // Leaving them is a valid answer, and the same level is not asked again until a subtopic joins or leaves it.
    // A merge keeps every block, body and citation.
    static async Task CrowdedSubtopicsReview()
    {
        static void SeedChain(MemoryStore store, TestClock clock, string id) =>
            Seed(store, clock, id, [("top", null, 1), ("chain", "top", 1), .. Enumerable.Range(0, 7).Select(i => ("leaf" + i, (string?)"chain", 2))]);
        // Every block with its text and citations as a reader sees them (saving stores citations as cite_* markers).
        static List<(string Topic, string Block, string Body, string Sources)> Blocks(Note note) => note.Sections
            .SelectMany(topic => topic.Blocks.Select(block => (topic.Id, block.Id, NoteSections.Expand(block.Body, note.Citations), string.Join(",", block.Anchors.SelectMany(x => x.SourceIds)))))
            .OrderBy(x => x.Item2).ToList();

        // Merge: leaf1's two blocks join leaf0, and leaf2 is renamed.
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; SeedChain(store, clock, "merge");
        await Configure(store, "merge", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        var before = Blocks(store.Notes["merge"]);
        var model = new OrganizationModel { ReplyFor = input =>
        {
            var review = input.TryGetProperty("review", out var value) ? value : default;
            Expect.That(review.ValueKind == JsonValueKind.Object && review.GetProperty("crowdedSubtopics").EnumerateArray().Any(x => x.GetProperty("parent").GetString() == "chain"),
                "The review input did not say which subtopics are over the threshold");
            return JsonSerializer.Serialize(new NotePatch { Moves = [new() { Block = "leaf1-b0", To = "leaf0" }, new() { Block = "leaf1-b1", To = "leaf0" }],
                Operations = [new() { Kind = "rename-topic", TopicId = "leaf2", Title = "Merged review" }] });
        } };
        await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, "merge");
        var merged = store.Notes["merge"];
        var after = Blocks(merged);
        Expect.That(model.Organizations == 1 && store.Organizations["merge"].DueReason == "crowded-subtopics" &&
            merged.Sections.Count(x => x.ParentId == "chain") == 6 && merged.Sections.All(x => x.Id != "leaf1") &&
            merged.Sections.Single(x => x.Id == "leaf0").Blocks.Select(x => x.Id).SequenceEqual(["leaf0-b0", "leaf0-b1", "leaf1-b0", "leaf1-b1"]) &&
            merged.Sections.Single(x => x.Id == "leaf2").Title == "Merged review",
            "A merge of two third-level subtopics was not applied: " + string.Join(" | ", merged.Sections.Select(x => x.Id + ":" + x.Blocks.Count)));
        Expect.That(after.Count == 16 && after.Select(x => (x.Block, x.Body, x.Sources)).SequenceEqual(before.Select(x => (x.Block, x.Body, x.Sources))) &&
            after.All(x => x.Body.Contains("[" + x.Sources + "]")),
            "A merge lost or changed a block body or citation");

        // Leave: the organizer keeps all seven; nothing changes, and the same level is not asked again after an unrelated change.
        clock = new TestClock(); store = new MemoryStore {Clock = clock}; SeedChain(store, clock, "leave");
        await Configure(store, "leave", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        before = Blocks(store.Notes["leave"]);
        model = new OrganizationModel { Reply = "{\"outline\":[],\"moves\":[],\"topicMoves\":[],\"operations\":[],\"deferred\":[]}" };
        await using (var agent = Agent(store, model, clock))
        {
            await Tick(agent, clock, 2, "leave");
            var left = store.Activity.Values.Single(x => x.SessionId == "leave" && x.Task == "Section organization");
            Expect.That(model.Organizations == 1 && left.Status == "completed" && Blocks(store.Notes["leave"]).SequenceEqual(before) &&
                store.Organizations["leave"].Status == "completed", "Leaving seven third-level subtopics as they are was not a completed review");
            var note = store.Notes["leave"];
            note.MeaningfulRevision++; note.RecentChanges.Add(new() { Revision = note.MeaningfulRevision, At = clock.GetUtcNow().UtcDateTime, TopicIds = ["leaf3"] });
            await Tick(agent, clock, 30, "leave");
            Expect.That(model.Organizations == 1, "Seven third-level subtopics the organizer left as they were were reviewed again without a new subtopic");
        }
    }
    // A third-level topic over six blocks (the v14 live run left one with seven, never reviewed) cannot take
    // subtopics, so it is reviewed with its parent: some of its blocks may move into a new sibling, or it stays.
    static async Task LargeThirdLevelTopic()
    {
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock};
        Seed(store, clock, "third", [("top", null, 1), ("chain", "top", 1), ("big", "chain", 7), ("small", "chain", 1)]);
        await Configure(store, "third", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        var model = new OrganizationModel { ReplyFor = input =>
        {
            var review = input.TryGetProperty("review", out var value) ? value : default;
            Expect.That(review.ValueKind == JsonValueKind.Object && review.GetProperty("largeTopics").EnumerateArray().Any(x => x.GetProperty("topic").GetString() == "big") &&
                input.GetProperty("editableSections").EnumerateArray().Any(x => x.GetProperty("id").GetString() == "chain"),
                "A large third-level topic was reviewed without its parent, so it could not get a sibling");
            return JsonSerializer.Serialize(new NotePatch { Outline = [new() { Key = "new1", Title = "Second part", Parent = "chain" }],
                Moves = [new() { Block = "big-b4", To = "new1" }, new() { Block = "big-b5", To = "new1" }, new() { Block = "big-b6", To = "new1" }] });
        } };
        await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, "third");
        var topics = store.Notes["third"].Sections;
        var sibling = topics.SingleOrDefault(x => x.Title == "Second part");
        Expect.That(model.Organizations == 1 && store.Organizations["third"].DueReason == "large-topic" && sibling?.ParentId == "chain" &&
            sibling.Blocks.Select(x => x.Id).SequenceEqual(["big-b4", "big-b5", "big-b6"]) && topics.Single(x => x.Id == "big").Blocks.Count == 4,
            "A third-level topic over six blocks was not reviewed, or its blocks did not move into a new sibling");
    }
    // "Full" (six blocks, or eight minutes of lecture) is a review threshold, not a limit (the owner's decision on
    // B). A block a reply still puts in a full topic is kept (NoteEditChecks.ClosedTopics); the topic is then over the
    // threshold and gets one organizer review. The organizer may split it, or leave it: leaving it is not a failure,
    // and the topic is not asked again until its blocks change. A topic full by time (its blocks span eight minutes
    // or more) is reviewed too, though it has fewer than seven blocks.
    static async Task FullTopicReview()
    {
        const string leaveReply = "{\"outline\":[],\"moves\":[],\"topicMoves\":[],\"operations\":[],\"deferred\":[]}";
        // Split.
        var clock = new TestClock(); var store = new MemoryStore {Clock = clock};
        Seed(store, clock, "split", ("full", null, 7));
        await Configure(store, "split", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        var model = new OrganizationModel { ReplyFor = _ => JsonSerializer.Serialize(new NotePatch { Outline = [new() { Key = "new1", Title = "Later part", Parent = "full" }],
            Moves = [new() { Block = "full-b5", To = "new1" }, new() { Block = "full-b6", To = "new1" }] }) };
        await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, "split");
        Expect.That(model.Organizations == 1 && store.Notes["split"].Sections.Single(x => x.Id == "full").Blocks.Count == 5 &&
            store.Notes["split"].Sections.Single(x => x.Title == "Later part").Blocks.Count == 2, "A topic over six blocks was not reviewed and split");

        // Leave: completed, nothing changes; a later change that keeps its blocks does not ask again, a new block does.
        clock = new TestClock(); store = new MemoryStore {Clock = clock};
        Seed(store, clock, "leave", ("full", null, 7));
        await Configure(store, "leave", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        model = new OrganizationModel { Reply = leaveReply };
        await using (var agent = Agent(store, model, clock))
        {
            await Tick(agent, clock, 2, "leave");
            Expect.That(model.Organizations == 1 && store.Notes["leave"].Sections.Single().Blocks.Count == 7 && store.Organizations["leave"].Status == "completed",
                "Leaving a full topic as it is was not a completed review");
            void Change(Action<Note> edit)
            {
                var note = store.Notes["leave"]; edit(note);
                note.MeaningfulRevision++; note.RecentChanges.Add(new() { Revision = note.MeaningfulRevision, At = clock.GetUtcNow().UtcDateTime, TopicIds = ["full"] });
            }
            Change(note => note.Sections.Single().Title = "Renamed full topic");
            await Tick(agent, clock, 30, "leave");
            Expect.That(model.Organizations == 1, "A full topic the organizer left unsplit was reviewed again although its blocks did not change");
            store.Speech("leave", "late", "A later fact."); store.Transcripts["leave"].Last().NoteStatus = "completed";
            Change(note => note.Sections.Single().Blocks.Add(new() { Id = "full-b7", Order = 7, Role = "detail", Body = "A later fact. [late]", CitationIds = ["late"],
                Anchors = [new() { Id = "full-p7", Length = "A later fact. [late]".Length, SourceIds = ["late"] }] }));
            await Tick(agent, clock, 30, "leave");
            Expect.That(model.Organizations == 2, "A full topic that gained a block after its review was not reviewed again");
        }

        // Full by time: three blocks spanning nine minutes.
        clock = new TestClock(); store = new MemoryStore {Clock = clock};
        Seed(store, clock, "time", ("long", null, 3));
        store.Transcripts["time"].Single(x => x.Id == "long-s2").StartMs = 9 * 60_000;
        await Configure(store, "time", new(CheckIntervalSeconds: 2, OrganizationCooldownSeconds: 15));
        model = new OrganizationModel { Reply = leaveReply };
        await using (var agent = Agent(store, model, clock)) await Tick(agent, clock, 2, "time");
        Expect.That(model.Organizations == 1 && store.Organizations["time"].DueReason == "large-topic", "A topic full by time that took another block was not reviewed");
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
        public Func<JsonElement, string>? ReplyFor;
        public override async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            if (name != "LectureSectionOrganizer") return await writer.Generate(name, instructions, prompt, ct, model, onUpdate, onUsage);
            Organizations++; LastOrganizationInput = prompt; Started.TrySetResult(); if (BlockOrganization) await Release.Task.WaitAsync(ct);
            if (Reply is not null) return Reply;
            using var doc = JsonDocument.Parse(prompt); var root = doc.RootElement; NotePatch patch;
            if (ReplyFor is not null) return ReplyFor(root);
            if (root.GetProperty("trigger").GetString() == "document-outline")
                patch = new() {TopicMoves = [new() {Topic = root.GetProperty("topicOutline")[1][0].GetString()!, To = root.GetProperty("topicOutline")[0][0].GetString()!}]};
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
