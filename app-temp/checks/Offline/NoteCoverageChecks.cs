using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;

// Note coverage: only a citation in the note body counts as covered speech, gap repair writes the earliest
// hole without reviving deletions, and a rewrite cannot quietly drop a formula it claims to retain.
static class NoteCoverageChecks
{
    public static async Task Run()
    {
        await GapRepair();
        Retention();
        Console.WriteLine("Note coverage checks passed: body citations only, earliest gap repaired without reviving deletions, stale repair rejected, formulas retained");
    }

    static async Task GapRepair()
    {
        var store = new MemoryStore();
        store.Add("gaps");
        for (var i = 0; i < 48; i++)
        {
            store.Speech("gaps", "early-" + i, "A substantive example of capacity sharing.");
            store.Transcripts["gaps"][i].NoteStatus = "completed";
        }
        store.Transcripts["gaps"][12].NoteStatus = "suppressed";
        store.Transcripts["gaps"][13].NoteStatus = "deferred";
        store.Notes["gaps"] = new Note
        {
            SessionId = "gaps",
            Version = 10,
            Markdown = "## Later\n\nLater fact. [early-47]",
            TranscriptIds = store.Transcripts["gaps"].Select(x => x.Id).ToList(),
            SuppressedSourceIds = ["early-11"]
        };
        var session = (await store.Session("gaps"))!;
        var audit = NoteCoverage.Audit(session);
        Expect.That(audit.Referenced == 1 && audit.Suppressed == 2 && audit.Unreferenced == 45 && audit.CompletedWithoutReference == 44 && audit.LargeGaps == 1,
            "Legacy completed metadata, deferral or intentional exclusion hid missing body references");
        Expect.That(NoteCoverage.OldestBatch(session).All(x => x.Id is not ("early-11" or "early-12" or "early-47")), "Repair resurrected an intentional exclusion");

        await using var agent = new NoteAgent(store, new FixtureNoteModel(), new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, new AiActivity(store));
        await agent.RepairCoverage("gaps", 10, CancellationToken.None);
        var repaired = (await store.Session("gaps"))!;
        Expect.That(repaired.NoteMarkdown.Contains("Later fact.") && NoteCoverage.Audit(repaired).Unreferenced < 45,
            "Repair failed to retain the latest content or bypass old completed statuses");
        await Expect.RejectsAsync(() => agent.RepairCoverage("gaps", 10, CancellationToken.None), "Stale repair accepted");
    }

    static void Retention()
    {
        var prior = new NoteSection
        {
            Id = "formula",
            Title = "Capacity",
            Markdown = "## Capacity\n\nR/n for n flows; n=10 yields R/10, assuming one shared bottleneck. [a, b]",
            Points = [new NotePoint { Id = "proof", Text = "R/n for n flows; n=10 yields R/10, assuming one shared bottleneck. [a, b]", SourceIds = ["a", "b"] }]
        };
        var note = new Note { SessionId = "s", Sections = [prior] };
        var deceptive = new NotePatch
        {
            Sections = [new SectionPatch { Id = "formula", BaseVersion = 1, Title = "Capacity", Markdown = "## Capacity\n\nFlows share capacity. [b, a]",
                Points = [new PointPatch { Text = "Flows share capacity. [b, a]", SourceIds = ["a", "b"], Retains = ["proof"] }] }]
        };
        var guarded = NoteSections.Apply("s", note, [prior], deceptive, [prior.Id], [], ["a", "b"], "organize", allowUserEdited: true);
        Expect.That(NoteSections.Render(guarded.Sections).Contains("n=10 yields R/10, assuming one shared bottleneck"),
            "Organizer claimed a retained ID and citations while deleting a formula/condition");
        Expect.Rejects(() => NoteSections.RequireRetention(note, new SectionUpdate([], [], []), ["a", "b"]),
            "Persistence boundary accepted deleting a whole saved section");
        NoteSections.RequireRetention(note, guarded, ["a", "b"]);
        var reordered = new NotePatch
        {
            Sections = [new SectionPatch { Id = "formula", BaseVersion = 1, Title = "Capacity", Markdown = prior.Markdown.Replace("[a, b]", "[b, a]"),
                Points = [new PointPatch { Text = prior.Points[0].Text.Replace("[a, b]", "[b, a]"), SourceIds = ["a", "b"], Retains = ["proof"] }] }]
        };
        var canonical = NoteSections.Apply("s", note, [prior], reordered, [prior.Id], [], ["a", "b"], "reorder");
        Expect.That(canonical.Sections[0].Points.Count == 1 && !canonical.Sections[0].Markdown.Contains("Earlier source-backed points"),
            "Citation reordering duplicated an unchanged old point");
    }
}
