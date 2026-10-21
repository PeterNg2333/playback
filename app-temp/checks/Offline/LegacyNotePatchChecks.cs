using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Playback.Infrastructure.Storage;

// The legacy note patch on stored BSON (no database): a placement-era version gets its anchor sources as block sources,
// a pre-tree version gets one block per topic, a second run changes nothing, and the patched pre-tree version restores.
static class LegacyNotePatchChecks
{
    public static async Task Run()
    {
        DocumentMappings.Register();
        const string id = "legacy";
        var allowed = new HashSet<string> { "t1", "t2", "t3" };
        var preTree = PreTree(id, "note_pretree", ["t1", "t2", "t3"]);
        var placement = Placement(id, "note_placement", ["t1", "t2", "t3"]);

        var store = new MemoryStore(); store.Add(id);
        foreach (var source in allowed) store.Speech(id, source, "Speech " + source);
        store.Versions[id] = [BsonSerializer.Deserialize<Note>(preTree), BsonSerializer.Deserialize<Note>(placement)];
        store.Notes[id] = BsonSerializer.Deserialize<Note>(placement);
        Expect.That(store.Notes[id].Sections.Single().Blocks.Single().SourceIds.Count == 0, "Fixture: an unpatched placement block already reads sources");
        await Expect.RejectsAsync(() => store.RestoreNote(id, 1, 2), "Fixture: an unpatched pre-tree version was restorable");

        var preTreeChange = LegacyNotePatch.Patch(preTree, allowed);
        var placementChange = LegacyNotePatch.Patch(placement, allowed);
        Expect.That(preTreeChange == new LegacyNotePatch.Change(0, 1, 1), "Pre-tree topics were not converted once each, or a heading-only topic got a block");
        Expect.That(placementChange == new LegacyNotePatch.Change(2, 0, 0), "Topic and segment blocks did not both get their anchor sources");

        var block = BsonSerializer.Deserialize<Note>(placement).Sections.Single().Blocks.Single();
        Expect.That(block.SourceIds.SequenceEqual(["t1", "t2", "t3"]), "Anchor sources were not merged in order without duplicates");
        Expect.That(BsonSerializer.Deserialize<Note>(placement).Segments.Single().Blocks.Single().SourceIds.SequenceEqual(["t3"]), "Segment block lost its anchor source");
        Expect.That(placement["Sections"][0]["Blocks"][0]["Anchors"].AsBsonArray.Count == 2 && placement.Contains("Coverage"), "The patch removed old fields");

        var note = BsonSerializer.Deserialize<Note>(preTree);
        var intro = note.Sections.Single(x => x.Id == "section_intro").Blocks.Single();
        var key = note.Citations.Single(x => x.SourceIds.SequenceEqual(["t1", "t2"])).Id;
        Expect.That(intro.Body == $"The kernel schedules threads. [{key}]\n\nQueues hold ready work. [cite_0123456789abcdef0123]", "Pre-tree body kept its heading or its sources were not compacted");
        Expect.That(intro is { Role: "note", UserEdited: false, Order: 0 } && intro.SourceIds.SequenceEqual(["t1", "t2", "t3"]), "Pre-tree block role, lock or point sources differ");
        Expect.That(intro.CitationIds.SequenceEqual([key, "cite_0123456789abcdef0123"]), "Pre-tree block citation IDs differ");
        Expect.That(note.Sections.Single(x => x.Id == "section_heading").Blocks.Count == 0, "A heading-only topic got an empty block");
        Expect.That(note.Markdown == "## 1 Intro\n\nThe kernel schedules threads. [t1, t2]" && preTree["Sections"][0]["Markdown"].IsString, "The patch changed Note.Markdown or removed the topic Markdown");
        NoteTree.Validate(note.Sections);

        var (preTreeOnce, placementOnce) = (preTree.DeepClone(), placement.DeepClone());
        Expect.That(!LegacyNotePatch.Patch(preTree, allowed).Any && !LegacyNotePatch.Patch(placement, allowed).Any &&
            preTree.Equals(preTreeOnce) && placement.Equals(placementOnce), "A second run changed the patched notes");

        store.Versions[id] = [BsonSerializer.Deserialize<Note>(preTree), BsonSerializer.Deserialize<Note>(placement)];
        store.Notes[id] = BsonSerializer.Deserialize<Note>(placement);
        await store.RestoreNote(id, 1, 2);
        var restored = (await store.Session(id))!.CurrentNote!;
        Expect.That(restored.Version == 3 && restored.Sections.SelectMany(x => x.Blocks).Single().SourceIds.SequenceEqual(["t1", "t2", "t3"]) &&
            restored.Markdown.Contains("The kernel schedules threads. [" + key + "]"), "The patched pre-tree version did not restore with its sources");
        await MarkdownOnly();
        Placeholders();
        Console.WriteLine("Legacy note patch checks passed: anchor sources on topic and segment blocks, pre-tree topics as one block with compacted citations, " +
            "Markdown-only versions split at headings, alias placeholders removed only beside other sources, old fields kept, idempotent, restore after patch (offline)");
    }

    // The earliest versions: only Note.Markdown, citing transcript IDs inline.
    static async Task MarkdownOnly()
    {
        const string id = "markdown";
        var allowed = new HashSet<string> { "t1", "t2", "t3" };
        var outline = new BsonDocument { { "_id", "note_md1" }, { "SessionId", id }, { "Version", 1 }, { "InputHash", "md1" }, { "Author", "agent" },
            { "Markdown", "# Lecture title\n\n## 1 Scheduling\n\nThreads run. [t1, t2]\n\n### Queues\n\nReady work. [t3; gone]\n\n#### Deep\n\nDeeper. [t3]\n\n" +
                "## Empty heading\n\n## Jump\n\n#### Skipped\n\nSkipped a level. [t2]\n\n```\n## not a heading\n```\n\n# Later top\n\nMore. [t1]" } };
        var flat = new BsonDocument { { "_id", "note_md2" }, { "SessionId", id }, { "Version", 2 }, { "InputHash", "md2" }, { "Author", "agent" }, { "Sections", new BsonArray() },
            { "Markdown", "# 語音記錄\n\n- 0:00 開場 [t1]\n- 0:30 提問 [t2]" } };
        var markdown = outline["Markdown"].AsString;

        Expect.That(LegacyNotePatch.Patch(outline, allowed) == new LegacyNotePatch.Change(0, 0, 0, 7), "Markdown-only version did not become seven topics");
        Expect.That(LegacyNotePatch.Patch(flat, allowed) == new LegacyNotePatch.Change(0, 0, 0, 1), "A version without topic headings did not become one topic");
        var topics = BsonSerializer.Deserialize<Note>(outline).Sections;
        NoteTree.Validate(topics);
        var rows = NoteTree.Outline(topics).Select(x => $"{x.Number} {x.Topic.Title}").ToList();
        Expect.That(rows.SequenceEqual(["1 Scheduling", "1.1 Queues", "1.1.1 Deep", "2 Empty heading", "3 Jump", "3.1 Skipped", "4 Later top"]),
            "Markdown headings gave the wrong topic tree: " + string.Join(" | ", rows));
        Topic("Scheduling", out var scheduling);
        var key = BsonSerializer.Deserialize<Note>(outline).Citations.Single(x => x.SourceIds.SequenceEqual(["t1", "t2"])).Id;
        Expect.That(scheduling.Body == $"Threads run. [{key}]" && scheduling.SourceIds.SequenceEqual(["t1", "t2"]) && scheduling is { Role: "note", UserEdited: false },
            "Markdown-only topic body, sources or lock differ");
        Topic("Queues", out var queues);
        Expect.That(queues.Body == "Ready work. [t3; gone]" && queues.SourceIds.SequenceEqual(["t3"]), "A citation with an unknown ID was compacted or lost its known source");
        Topic("Skipped", out var skipped);
        Expect.That(skipped.Body.StartsWith("Skipped a level.") && skipped.Body.Contains("## not a heading"), "A heading inside a code fence split a topic");
        Expect.That(topics.Single(x => x.Title == "Empty heading").Blocks.Count == 0 && outline["Markdown"].AsString == markdown, "Empty heading got a block or Note.Markdown changed");
        var single = BsonSerializer.Deserialize<Note>(flat).Sections.Single();
        Expect.That(single.Title == "語音記錄" && single.Blocks.Single().SourceIds.SequenceEqual(["t1", "t2"]), "The leading title did not name the only topic");

        var (outlineOnce, flatOnce) = (outline.DeepClone(), flat.DeepClone());
        Expect.That(!LegacyNotePatch.Patch(outline, allowed).Any && !LegacyNotePatch.Patch(flat, allowed).Any && outline.Equals(outlineOnce) && flat.Equals(flatOnce),
            "A second run changed a converted Markdown-only version");

        var store = new MemoryStore(); store.Add(id);
        foreach (var source in allowed) store.Speech(id, source, "Speech " + source);
        store.Versions[id] = [BsonSerializer.Deserialize<Note>(outline), BsonSerializer.Deserialize<Note>(flat)];
        store.Notes[id] = BsonSerializer.Deserialize<Note>(flat);
        await store.RestoreNote(id, 1, 2);
        var restored = (await store.Session(id))!.CurrentNote!;
        Expect.That(restored.Version == 3 && restored.Sections.Count == 7 && restored.Sections.Single(x => x.Title == "Deep").Blocks.Single().SourceIds.SequenceEqual(["t3"]),
            "A converted Markdown-only version did not restore with its topics and sources");

        void Topic(string title, out NoteBlock block) => block = topics.Single(x => x.Title == title).Blocks.Single();
    }

    // [cite_T009] was a placement-era alias the model left in the text; its source is cited beside it.
    static void Placeholders()
    {
        var real = "cite_" + new string('a', 20);
        var note = new BsonDocument { { "_id", "note_alias" }, { "SessionId", "alias" }, { "Version", 1 }, { "Markdown", "kept" },
            { "Citations", new BsonArray { new BsonDocument { { "_id", real }, { "SourceIds", new BsonArray { "t1", "t2" } } } } },
            { "Sections", new BsonArray { new BsonDocument { { "_id", "section_alias" }, { "Title", "Alias" }, { "Order", 0 }, { "Blocks", new BsonArray {
                new BsonDocument { { "_id", "block_cited" }, { "Order", 0 }, { "Body", $"Fields multiply. [cite_T009] [cite_T010] [{real}]\n\nRaces. [cite_new1] [{real}]" },
                    { "CitationIds", new BsonArray { "cite_T009", "cite_T010", real, "cite_new1" } }, { "SourceIds", new BsonArray { "t1", "t2" } } },
                new BsonDocument { { "_id", "block_bare" }, { "Order", 1 }, { "Body", "Only an alias. [cite_T011]" },
                    { "CitationIds", new BsonArray { "cite_T011" } }, { "SourceIds", new BsonArray() } } } } } } } };
        Expect.That(LegacyNotePatch.Patch(note, new HashSet<string> { "t1", "t2" }) == new LegacyNotePatch.Change(0, 0, 0, 0, 3), "Alias placeholders were not removed exactly three times");
        var blocks = BsonSerializer.Deserialize<Note>(note).Sections.Single().Blocks;
        Expect.That(blocks[0].Body == $"Fields multiply. [{real}]\n\nRaces. [{real}]" && blocks[0].CitationIds.SequenceEqual([real]), "Placeholders left text behind or citation IDs were not updated");
        Expect.That(blocks[1].Body == "Only an alias. [cite_T011]", "A placeholder was removed from a block with no other source");
        Expect.That(!LegacyNotePatch.Patch(note, new HashSet<string> { "t1", "t2" }).Any, "A second run removed more placeholders");
    }

    // Saved before topic notes: each topic is Markdown with points. t holds three transcript IDs.
    public static BsonDocument PreTree(string id, string documentId, string[] t) => new()
    {
        { "_id", documentId }, { "SessionId", id }, { "Version", 1 }, { "SchemaVersion", 1 }, { "InputHash", "pretree" },
        { "Markdown", "## 1 Intro\n\nThe kernel schedules threads. [" + t[0] + ", " + t[1] + "]" }, { "Author", "agent" }, { "Presentation", "numbered-hierarchy" },
        { "TranscriptIds", new BsonArray { t[0], t[1], t[2] } }, { "InputTranscriptIds", new BsonArray { t[0] } },
        { "Citations", new BsonArray { new BsonDocument { { "_id", "cite_0123456789abcdef0123" }, { "SourceIds", new BsonArray { t[2] } } } } },
        { "Sections", new BsonArray {
            new BsonDocument { { "_id", "section_intro" }, { "SchemaVersion", 1 }, { "Version", 1 }, { "Title", "Intro" }, { "Order", 0 },
                { "Markdown", "## Intro\n\nThe kernel schedules threads. [" + t[0] + ", " + t[1] + "]\n\nQueues hold ready work. [cite_0123456789abcdef0123]\n" },
                { "Points", new BsonArray {
                    new BsonDocument { { "_id", "p1" }, { "Text", "The kernel schedules threads." }, { "SourceIds", new BsonArray { t[0], t[1] } } },
                    new BsonDocument { { "_id", "p2" }, { "Text", "Queues hold ready work." }, { "SourceIds", new BsonArray { t[1], t[2] } } } } } },
            new BsonDocument { { "_id", "section_heading" }, { "SchemaVersion", 1 }, { "Version", 1 }, { "Title", "Heading" }, { "Order", 1 },
                { "Markdown", "## Heading\n\n" }, { "Points", new BsonArray() } } } }
    };

    // Placement era: blocks keep their sources in offset anchors, in topics and in segments.
    public static BsonDocument Placement(string id, string documentId, string[] t) => new()
    {
        { "_id", documentId }, { "SessionId", id }, { "Version", 2 }, { "SchemaVersion", 2 }, { "InputHash", "placement" },
        { "Markdown", "## 1 Scheduling\n\nThreads run on cores." }, { "Author", "agent" },
        { "Coverage", new BsonArray { new BsonDocument { { "SourceId", t[0] }, { "Status", "covered" } } } }, { "SuppressedSourceIds", new BsonArray() },
        { "Sections", new BsonArray {
            new BsonDocument { { "_id", "section_sched" }, { "SchemaVersion", 2 }, { "Version", 3 }, { "Title", "Scheduling" }, { "Order", 0 },
                { "Blocks", new BsonArray { new BsonDocument { { "_id", "block_sched" }, { "Order", 0 }, { "Role", "legacy" }, { "Body", "Threads run on cores." },
                    { "UserEdited", false }, { "CitationIds", new BsonArray() },
                    { "Anchors", new BsonArray {
                        new BsonDocument { { "_id", "a1" }, { "Offset", 0 }, { "Length", 7 }, { "SourceIds", new BsonArray { t[0], t[1] } } },
                        new BsonDocument { { "_id", "a2" }, { "Offset", 8 }, { "Length", 3 }, { "SourceIds", new BsonArray { t[1], t[2] } } } } } } } } } } },
        { "Segments", new BsonArray { new BsonDocument { { "Index", 0 }, { "Title", "Scheduling" }, { "InputHash", "w0" },
            { "Blocks", new BsonArray { new BsonDocument { { "_id", "seg_block" }, { "Body", "Cores." },
                { "Anchors", new BsonArray { new BsonDocument { { "_id", "a3" }, { "Offset", 0 }, { "Length", 6 }, { "SourceIds", new BsonArray { t[2] } } } } } } } } } } }
    };
}
