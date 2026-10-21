using Playback.Application.Activity;
using Playback.Application.Notes;
using Playback.Domain.Models;
using Playback.Domain.Notes;

// The three-speed note workflow over an in-memory store with the fixture model: jots while recording, segments as windows
// close, the finish after Stop, and updates that touch only what a changed transcript fed.
static class ThreeSpeedNoteChecks
{
    public static async Task Run()
    {
        await Recording();
        await Corrections();
        await LockedBlocksAndRebuild();
        await PolishGuard();
        await Backoff();
        Aliases();
        Console.WriteLine("Three-speed note checks passed: a segment per closed five minutes before the last minute's jot, jots replaced by their segment, " +
            "Stop writes the open window and outlines and polishes it into finished topics with a summary, a corrected transcript rewrites only its window " +
            "and polishes only the topic it fed, locked blocks survive a rebuild, instructions rewrite a block but not a locked one, a polish that drops " +
            "citations keeps the segment blocks, failures back off, aliases decode (fixture model only)");
    }

    // A transcript line at a time in the lecture, confirmed now by the check's clock.
    static void Line(MemoryStore store, TestClock clock, string id, string key, double seconds, string text) => store.Transcripts[id].Add(new Transcript
    {
        Id = key, SessionId = id, SourceId = "mic", Original = text, StartMs = (long)(seconds * 1000), EndMs = (long)(seconds * 1000) + 8000,
        NoteStatus = "pending", ConfirmedAt = clock.GetUtcNow().UtcDateTime
    });

    // The first window (0:00–4:50) and the start of the second.
    static (MemoryStore Store, TestClock Clock, FixtureNoteModel Model, NoteWorkflow Notes) Lecture(string id)
    {
        var store = new MemoryStore(); store.Add(id);
        var clock = new TestClock();
        var model = new FixtureNoteModel();
        for (var s = 0; s < 300; s += 10) Line(store, clock, id, $"{id}-{s}", s, $"Kernel point {s}");
        Line(store, clock, id, $"{id}-301", 301, "Second window starts");
        return (store, clock, model, new NoteWorkflow(new Playback.Api.Notes.NoteStoreAdapter(store), new Playback.Api.Notes.NoteStoreAdapter(store),
            new Playback.Api.Notes.GeminiNoteWriter(model), new Playback.Api.Notes.NoteActivityAdapter(new AiActivity(store),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Playback.Api.Notes.NoteAgent>.Instance), new Playback.Api.Notes.OpenccNoteScript(), clock));
    }

    static async Task Step(NoteWorkflow notes, TestClock clock, string id, double seconds = 3)
    {
        clock.Advance(seconds);
        notes.Tick([id], CancellationToken.None);
        await notes.Drain();
    }

    static Note Latest(MemoryStore store, string id) => store.Notes[id];

    static async Task Recording()
    {
        var (store, clock, model, notes) = Lecture("rec");
        await Step(notes, clock, "rec");
        var note = Latest(store, "rec");
        Expect.That(note.Segments.Count == 1 && note.Segments[0].Blocks.Count == 30 && note.Sections.Any(x => x.Id == "segment-0"),
            "A closed five-minute window was not written as one segment shown as a draft topic");
        Expect.That(store.Transcripts["rec"].Where(x => x.StartMs < 300_000).All(x => x.NoteStatus == "completed"), "Segment sources were not marked completed");
        Expect.That(note.Segments[0].Blocks.All(b => b.SourceIds.Count == 1) && note.Markdown.Contains("cite_"),
            "Segment blocks lost their single cited source");

        Line(store, clock, "rec", "rec-330", 330, "Middle of the second window");
        Line(store, clock, "rec", "rec-365", 365, "Minute six");
        await Step(notes, clock, "rec");
        note = Latest(store, "rec");
        Expect.That(note.Sections.Single(x => x.Id == NoteLayout.LiveTopicId).Blocks.Single().Id == NoteLayout.LiveBlockId(5) && model.Calls["NoteLiveJot"] == 1,
            "The last finished minute was not jotted while recording");
        await Step(notes, clock, "rec");
        Expect.That(model.Calls["NoteLiveJot"] == 1, "A minute was jotted twice");

        await store.RequestNoteFlush("rec");
        await Step(notes, clock, "rec");
        note = Latest(store, "rec");
        Expect.That(note.Segments.Count == 2 && note.Sections.All(x => x.Id != NoteLayout.LiveTopicId), "Stop did not write the open window or replace its jots");
        await Step(notes, clock, "rec");
        note = Latest(store, "rec");
        Expect.That(note.Sections.Count == 2 && note.Sections.All(NoteLayout.IsFinished) && note.Sections.All(x => x.Blocks.Single().Id.StartsWith("p-")),
            "Stop did not outline and polish the segments into finished topics");
        Expect.That(note.Summary.SequenceEqual(["Fixture summary"]) && note.ReviewQuestions.Count == 1 && note.Markdown.Contains("Polished."),
            "The finish did not save the summary, questions and polished text");
        Expect.That(!store.Gates["rec"].FlushRequested, "Stop was not acknowledged after the finish");
        var calls = model.TotalCalls;
        await Step(notes, clock, "rec", 120);
        Expect.That(model.TotalCalls == calls, "A finished, unchanged session called the model again");

        var target = note.Sections[0].Blocks[0].Id;
        var instructed = await notes.Instruct("rec", "Shorter", null, target, note.Version, CancellationToken.None);
        Expect.That(instructed.Applied == 1 && Latest(store, "rec").Sections[0].Blocks[0].Body.StartsWith("Rewritten:"), "An instruction did not rewrite its block");
        await new NoteCommands(new Playback.Api.Notes.NoteStoreAdapter(store), new Playback.Api.Notes.NoteStoreAdapter(store))
            .LockBlock("rec", Latest(store, "rec").Version, target, true);
        var locked = await notes.Instruct("rec", "Again", null, target, Latest(store, "rec").Version, CancellationToken.None);
        Expect.That(locked.Applied == 0 && locked.NotApplied.Single().EndsWith("locked"), "An instruction changed a locked block");
    }

    static async Task Corrections()
    {
        var (store, clock, model, notes) = Lecture("fix");
        await store.RequestNoteFlush("fix");
        await Step(notes, clock, "fix");
        await Step(notes, clock, "fix");
        await Step(notes, clock, "fix");
        var before = Latest(store, "fix");
        Expect.That(before.Sections.Count == 2 && before.Sections.All(NoteLayout.IsFinished), "The lecture did not finish");
        var first = before.Sections.Single(x => x.SegmentBlockIds.All(id => NoteLayout.SegmentOf(id) == 0));

        var row = store.Transcripts["fix"].Single(x => x.Id == "fix-301");
        row.Original = "Second window corrected by a rerun";
        row.NoteStatus = "pending";
        clock.Advance(61);
        await Step(notes, clock, "fix");
        var rewritten = Latest(store, "fix");
        Expect.That(rewritten.Segments.Single(x => x.Index == 1).Blocks.Single().Body.Contains("corrected by a rerun") &&
            rewritten.Segments.Single(x => x.Index == 0).InputHash == before.Segments.Single(x => x.Index == 0).InputHash,
            "A corrected transcript did not rewrite exactly its own window");
        await Step(notes, clock, "fix");
        var after = Latest(store, "fix");
        Expect.That(after.Sections.Single(x => x.Id == first.Id).Blocks.Single().Body == first.Blocks.Single().Body,
            "A topic the corrected window did not feed was polished again");
        Expect.That(after.Sections.Single(x => x.Id != first.Id).Blocks.Single().Body.Contains("corrected by a rerun") && model.Calls["NoteOutline"] == 1,
            "The topic the corrected window fed was not polished again without a new outline");
    }

    static async Task LockedBlocksAndRebuild()
    {
        var (store, clock, _, notes) = Lecture("lock");
        await notes.Generate("lock", CancellationToken.None);
        var note = Latest(store, "lock");
        var block = note.Sections[0].Blocks[0];
        var commands = new NoteCommands(new Playback.Api.Notes.NoteStoreAdapter(store), new Playback.Api.Notes.NoteStoreAdapter(store));
        await commands.EditBlock("lock", note.Version, block.Id, "My own wording [lock-0]");
        var result = await notes.Rebuild("lock", CancellationToken.None);
        var rebuilt = Latest(store, "lock");
        var kept = rebuilt.Sections.SelectMany(x => x.Blocks).Where(x => x.UserEdited).ToList();
        Expect.That(result.Rounds == 2 && kept.Count == 1 && kept[0].Id == block.Id && kept[0].Body.StartsWith("My own wording"),
            "A rebuild lost or changed an edited block");
        Expect.That(rebuilt.Sections.All(NoteLayout.IsFinished) && rebuilt.Segments.Count == 2, "A rebuild did not write and finish every window again");
        Expect.That(store.Versions["lock"].Any(x => x.Author == "user rebuild"), "A rebuild did not keep the earlier notes in history");
    }

    static async Task PolishGuard()
    {
        var (store, clock, model, notes) = Lecture("guard");
        model.PolishDropsCitations = true;
        await notes.Generate("guard", CancellationToken.None);
        var note = Latest(store, "guard");
        Expect.That(note.Sections.All(NoteLayout.IsFinished) && note.Sections.SelectMany(x => x.Blocks).All(b => b.Id.StartsWith("s")),
            "A polish that dropped citations replaced the segment blocks");
    }

    static async Task Backoff()
    {
        var (store, clock, model, notes) = Lecture("fail");
        model.FailNext = true;
        await Step(notes, clock, "fail");
        var calls = model.TotalCalls;
        await Step(notes, clock, "fail");
        Expect.That(model.TotalCalls == calls && !store.Notes.ContainsKey("fail"), "A failed step was tried again before its backoff");
        await Step(notes, clock, "fail", 11);
        Expect.That(model.TotalCalls == calls + 1 && store.Notes["fail"].Segments.Count == 1, "A failed step was not tried again after its backoff");
    }

    static void Aliases()
    {
        var aliases = new NoteAliases(["a", "b", "c"]);
        Expect.That(aliases.Decode("x [T1, T9] y [T2-T3] z [ref:abc] w [T9] `[T1]`") == "x [a] y [b, c] z [ref:abc] w `[T1]`",
            "Aliases were not decoded, expanded and dropped as expected");
        Expect.That(aliases.Encode("cite [a, c] and [ref:abc]") == "cite [T1, T3] and [ref:abc]", "Source IDs were not encoded as aliases");
    }
}
