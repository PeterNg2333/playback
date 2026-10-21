using MongoDB.Bson;
using MongoDB.Bson.Serialization;

// The legacy note patch on stored BSON (no database): a placement-era version gets its anchor sources as block sources,
// a pre-tree version gets one block per topic, a second run changes nothing, and the patched pre-tree version restores.
static class LegacyNotePatchChecks
{
    public static async Task Run()
    {
        Playback.Infrastructure.Storage.DocumentMappings.Register();
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
        Console.WriteLine("Legacy note patch checks passed: anchor sources on topic and segment blocks, pre-tree topics as one block with compacted citations, old fields kept, idempotent, restore after patch (offline)");
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
