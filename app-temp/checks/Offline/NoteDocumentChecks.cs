using System.Text.Json;
using Playback.Api;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Sources;

// The note document: how a model's section patch is applied, what a user edit deletes on purpose,
// what recovery may bring back, which citations and sources are valid, and the edit log.
static class NoteDocumentChecks
{
    public static void Run()
    {
        SectionPatches();
        UserEditsAndRecovery();
        References();
        EditLog();
        NextSources();
        Console.WriteLine("Note document checks passed: retained sections and formulas, literal new identities, pending/deferred sources, " +
            "persistent citations, intentional deletions, recovery, reference validation, edit log, bounded next sources");
    }

    static (NoteSection Early, NoteSection Security, Note Note) TwoSections()
    {
        var early = new NoteSection
        {
            Id = "fair",
            Title = "Fair sharing",
            Markdown = "## Fair sharing\n\nThe target is R/n for n flows sharing one bottleneck; n=10 gives R/10. [early]",
            Points = [new NotePoint { Id = "formula", Text = "The target is R/n for n flows sharing one bottleneck; n=10 gives R/10. [early]", SourceIds = ["early"] }]
        };
        var security = new NoteSection
        {
            Id = "security",
            Title = "Security",
            Markdown = "## Security\n\nEncryption and encapsulation are distinct. [security-source]",
            Points = [new NotePoint { Id = "layers", Text = "Encryption and encapsulation are distinct. [security-source]", SourceIds = ["security-source"] }]
        };
        var note = new Note { SessionId = "s", Id = "old", Version = 112, Sections = [early, security], Markdown = NoteSections.Render([early, security]) };
        return (early, security, note);
    }

    static void SectionPatches()
    {
        var (early, security, note) = TwoSections();
        var add = new NotePatch
        {
            Sections = [new SectionPatch { Id = "new", Title = "Edge", Markdown = "## Edge\n\nNew edge topic. [late]",
                Points = [new PointPatch { Text = "New edge topic. [late]", SourceIds = ["late"] }] }]
        };
        var update = NoteSections.Apply("s", note, note.Sections, add, [], ["late"], ["early", "security-source", "late"], "hash");
        Expect.That(update.Sections[0] == early && update.Sections[1] == security && NoteSections.Render(update.Sections).Contains("R/10"),
            "Appending a topic removed an old formula/section");
        Expect.That(update.Coverage.Single().PointIds.Count == 1 && update.Citations.Single().SourceIds.SequenceEqual(["late"]),
            "Coverage or durable citation was not tied to the actual point");

        var multipleNew = new NotePatch
        {
            Sections = [add.Sections[0], new SectionPatch { Id = "new", Title = "Continuation", Markdown = "## Continuation\n\nAnother complete topic. [later]",
                Points = [new PointPatch { Text = "Another complete topic. [later]", SourceIds = ["later"] }] }]
        };
        var appended = NoteSections.Apply("s", note, note.Sections, multipleNew, [], ["late", "later"], ["early", "security-source", "late", "later"], "two-new");
        Expect.That(appended.Sections.Count == 4 && appended.Sections.Select(x => x.Id).Distinct().Count() == 4 && appended.Coverage.Count == 2,
            "Two new topics using the literal new identity collided or lost coverage");
        var partial = NoteSections.Apply("s", note, note.Sections, add, [], ["late", "unaddressed"], ["early", "security-source", "late", "unaddressed"], "partial");
        Expect.That(partial.Coverage.Single(x => x.SourceId == "unaddressed").Status == "pending" &&
            partial.Coverage.Single(x => x.SourceId == "unaddressed").PointIds.Count == 0 && partial.Sections.Count == 3,
            "A valid partial section was discarded, or an omitted source was falsely marked completed/deferred");
        Expect.Rejects(() => NoteSections.Apply("s", note, note.Sections, new NotePatch { Sections = [new SectionPatch { Id = "invented-topic" }] }, [], [], [], "x"),
            "An invented section identity was accepted");

        var bad = new NotePatch
        {
            Sections = [new SectionPatch { Id = "fair", BaseVersion = 1, Title = "Fair", Markdown = "## Fair\n\nSome sharing. [early]",
                Points = [new PointPatch { Text = "Some sharing. [early]", SourceIds = ["early"] }] }]
        };
        Expect.Rejects(() => NoteSections.Apply("s", note, note.Sections, bad, ["fair"], [], ["early"], "x"), "Dropping a coverage point was accepted");
        bad.Sections[0].Points[0].Retains = ["formula"];
        var safeguarded = NoteSections.Apply("s", note, note.Sections, bad, ["fair"], [], ["early"], "y");
        Expect.That(NoteSections.Render(safeguarded.Sections).Contains("R/n") && NoteSections.Render(safeguarded.Sections).Contains("n=10"),
            "A model retaining an ID while dropping its formula bypassed literal retention");
        early.UserEdited = true;
        Expect.Rejects(() => NoteSections.Apply("s", note, note.Sections, bad, ["fair"], [], ["early"], "x"), "Automatic generation replaced a user-edited section");
        early.UserEdited = false;
        Expect.Rejects(() => NoteSections.Apply("s", note, note.Sections, new NotePatch(), [], ["late"], ["late"], "x"), "Metadata-only completion was accepted");
        var deferred = NoteSections.Apply("s", note, note.Sections,
            new NotePatch { Deferred = [new SourceDisposition { SourceId = "half", Reason = "Formula continues in the next chunk" }] }, [], ["half"], ["half"], "x");
        Expect.That(deferred.Coverage.Single().Status == "deferred", "Incomplete fragments were completed");

        var compact = NoteSections.Compact("s", "One point [early, security-source]", ["early", "security-source"], update.Citations);
        Expect.That(NoteSections.Expand(compact, update.Citations) == "One point [early, security-source]" &&
            NoteSections.Compact("s", "One point [security-source, early]", ["early", "security-source"], update.Citations) == compact,
            "Persistent citation identity depended on ordering or renumbering");

        var material = new Material { Id = new string('b', 32), Text = new string('x', 14500), Name = "Long material" };
        var passages = MaterialSources.Passages([material]).ToList();
        Expect.That(passages.Count == 5 && string.Concat(passages.Select(x => x.Text)) == material.Text && passages.All(x => x.Text.Length <= 3000) &&
            passages.All(x => MaterialSources.Parent(x.Id) == material.Id), "Bounded material passages lost text or source identity");
    }

    static void UserEditsAndRecovery()
    {
        var (_, security, note) = TwoSections();
        var deleted = new List<string>();
        var suppressed = new List<string>();
        var edited = NoteSections.UserEdit(note, "s", security.Markdown, ["early", "security-source"], [], deleted, suppressed);
        Expect.That(deleted.Contains("fair") && suppressed.Contains("early") && edited.Single() == security,
            "Intentional deletion was not recorded: " + JsonSerializer.Serialize(new { deleted, suppressed, edited }));
        var latest = new Note { Id = "new", Version = 113, SessionId = "s", Sections = edited, DeletedSectionIds = deleted, SuppressedSourceIds = suppressed };
        Expect.That(!NoteSections.Recover(note, latest, ["early", "security-source"]).Candidates.Any(x => x.MissingSourceIds.Contains("early")),
            "Recovery revived an intentional deletion");
        latest.DeletedSectionIds.Clear();
        latest.SuppressedSourceIds.Clear();
        var preview = NoteSections.Recover(note, latest, ["early", "security-source"]);
        Expect.That(preview.Candidates.Single().Markdown.Contains("R/10") && preview.CurrentSections.Single() == security && latest.Version == 113,
            "Recovery preview overwrote current notes or lost the formula");
    }

    static void References()
    {
        var allowedTerm = new TermInsight { Id = "a".PadRight(64, 'a') };
        NoteAgent.ValidateReferences($"A term [ref:{allowedTerm.Id}]", [allowedTerm]);
        Expect.Rejects(() => NoteAgent.ValidateReferences("Invented [ref:missing]", [allowedTerm]), "Invented note reference was accepted");
        NoteAgent.ValidateReferences("Literal `[ref:unknown]`", []);

        var longNoteSource = new string('c', 32) + "-microphone-63926191107278790-" + new string('d', 64);
        var secondNoteSource = new string('e', 32);
        NoteAgent.ValidateSourceReferences($"Point [{longNoteSource}, {secondNoteSource}] [unclear] [ref:{allowedTerm.Id}]", [longNoteSource, secondNoteSource]);
        Expect.Rejects(() => NoteAgent.ValidateSourceReferences($"Point [{longNoteSource}, {new string('f', 32)}]", [longNoteSource]),
            "An invented source in a mixed citation was accepted");
        NoteAgent.ValidateSourceReferences($"Code `[{new string('f', 32)}]`\n```text\n[{new string('f', 32)}]\n```", []);
        foreach (var marker in new[] { "cite_T003, T004", "T999", "cite_deadbeef", "cite_" })
            Expect.Rejects(() => NoteAgent.ValidateSourceReferences($"Point [{marker}]", []),
                "An unresolved prompt alias or fabricated citation was accepted: " + marker);
        var prompt = new NotePrompt("", new() { ["T001"] = longNoteSource, ["T002"] = secondNoteSource });
        var decoded = prompt.Decode("Point [T001; T002]. Literal `[T001]`.\n```text\n[T002]\n```\n[T001](https://example.com)");
        Expect.That(decoded == $"Point [{longNoteSource}, {secondNoteSource}]. Literal `[T001]`.\n```text\n[T002]\n```\n[T001](https://example.com)",
            "Note aliases were not decoded consistently with code and Markdown link boundaries");
        NoteAgent.ValidateSourceReferences(decoded, [longNoteSource, secondNoteSource]);
        NoteAgent.ValidateSourceReferences("Literal `[cite_T003, T004]`\n```text\n[cite_unknown]\n```\n[cite_T001](https://example.com)", []);

        var widePassage = LectureFixtures.WidestPassage();
        var wideCitation = "Point [" + string.Join(", ", widePassage.Select(x => x.Id)) + "]";
        Expect.Rejects(() => NoteAgent.ValidateSourceReferences(wideCitation.Replace(widePassage[^1].Id, new string('f', 32)), widePassage.Select(x => x.Id)),
            "An invented source at the end of a long merged citation was accepted");
    }

    static void EditLog()
    {
        var noteEdits = NoteChangeLog.Build("Alpha [old]\nKeep", "Alpha [old]\nInserted [new]\nKeep", ["old", "new"], []);
        Expect.That(noteEdits.Count == 1 && noteEdits[0].Kind == "insert" && noteEdits[0].Line == 2 &&
            noteEdits[0].Text == "Inserted [new]" && noteEdits[0].TranscriptIds.SequenceEqual(["new"]),
            "Note edit log must locate an inserted line and its cited transcript");
        var removedEdits = NoteChangeLog.Build("Remove [old]\nKeep", "Keep", ["old"], []);
        Expect.That(removedEdits.Count == 1 && removedEdits[0].Kind == "remove" && removedEdits[0].TranscriptIds.SequenceEqual(["old"]),
            "Removed note lines must keep their former source citation");
        var bulkEdits = NoteChangeLog.Build("", string.Join('\n', Enumerable.Repeat("Line [new]", 3_100)), ["new"], []);
        Expect.That(bulkEdits.Count == 1 && bulkEdits[0].Kind == "insert" && bulkEdits[0].TranscriptIds.SequenceEqual(["new"]),
            "Large notes must keep a bounded edit log with its cited source");

        var longNoteSource = new string('c', 32) + "-microphone-63926191107278790-" + new string('d', 64);
        var secondNoteSource = new string('e', 32);
        var longSourceEdits = NoteChangeLog.Build("", $"Point [[{longNoteSource}, {secondNoteSource}]]", [longNoteSource], [secondNoteSource]);
        Expect.That(longSourceEdits.Single().TranscriptIds.SequenceEqual([longNoteSource]) && longSourceEdits.Single().MaterialIds.SequenceEqual([secondNoteSource]),
            "Long and grouped citations must retain both transcript and material provenance");
        var widePassage = LectureFixtures.WidestPassage();
        var wideCitation = "Point [" + string.Join(", ", widePassage.Select(x => x.Id)) + "]";
        Expect.That(NoteChangeLog.Build("", wideCitation, widePassage.Select(x => x.Id), []).Single().TranscriptIds.Count == 16,
            "The maximum merged passage must keep all of its canonical source IDs in the edit log");
    }

    static void NextSources()
    {
        var backlog = Enumerable.Range(0, 45).Select(i => new Transcript
        {
            Id = $"late-{i}",
            StartMs = i * 1000,
            Original = "source text",
            NoteStatus = i == 1 ? "completed" : i == 2 ? "failed" : "pending"
        }).ToList();
        backlog.Add(new Transcript { Id = "empty", StartMs = 1, Original = "", NoteStatus = "pending" });
        var pendingNotes = NoteInput.Pending(backlog);
        Expect.That(pendingNotes.Count == 44 && pendingNotes.Count <= 64 && pendingNotes.Sum(x => x.SourceText.Length) <= 18000 &&
            pendingNotes[0].Id == "late-0" && pendingNotes.Any(x => x.Id == "late-2") && pendingNotes.All(x => x.Id != "late-1" && x.Id != "empty"),
            "Note jobs must include late and failed transcripts in bounded batches without using a time cursor");

        var noteMaterials = Enumerable.Range(0, 7).Select(i => new Material { Id = $"material-{i}", Text = "synthetic" }).ToList();
        var priorNote = new Note
        {
            MaterialIds = noteMaterials.Take(5).Select(x => x.Id).ToList(),
            Coverage = noteMaterials.Take(5)
                .Select(x => new SourceDisposition { SourceId = x.Id, Status = "covered", ContentHash = ContentHash.Of(x.Text), PointIds = ["written"] }).ToList(),
            Sections = [new NoteSection { Points = [new NotePoint { Id = "written", SourceIds = noteMaterials.Take(5).Select(x => x.Id).ToList() }] }]
        };
        Expect.That(NoteInput.Materials(noteMaterials, null).Count == 2 &&
            NoteInput.Materials(noteMaterials, priorNote).Select(x => x.Id).SequenceEqual(["material-5", "material-6"]) &&
            NoteInput.Materials(noteMaterials, new Note { MaterialIds = priorNote.MaterialIds }).First().Id == "material-0",
            "Material-ID metadata cannot prove digestion; bounded actual point coverage chooses new passages");
    }
}
