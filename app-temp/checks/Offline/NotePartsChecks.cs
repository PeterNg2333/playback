using System.Security.Cryptography;
using MongoDB.Bson;
using Playback.Infrastructure.Storage;

// Note history with shared parts: a 165-minute lecture written like the three-speed notes (a live jot each minute, a
// segment and its topic each five minutes, then an outline and a polish per topic after Stop), every version saved
// through NoteMutation as the repository does. Each version must read back byte for byte; storage must shrink.
static class NotePartsChecks
{
    public static void Run()
    {
        DocumentMappings.Register();
        var session = new string('e', 32);
        // Real chunk IDs (about 125 characters), eight per minute.
        string Chunk(int index) => ChunkIdentity.Id(session, "microphone", 639_000_000_000_000_000 + index * 80_000_000L,
            Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(index))));
        string Text(int seed, int length) => string.Concat(Enumerable.Range(0, length).Select(i => (char)(0x4E00 + (seed * 7919 + i * 104729) % 20000)));
        NoteBlock Block(string id, int order, int minute, int length) => new()
        {
            Id = id, Order = order,
            Body = Text(minute * 31 + order, length) + $" [{Chunk(minute * 8)}, {Chunk(minute * 8 + 5)}]",
            SourceIds = Enumerable.Range(minute * 8, 8).Select(Chunk).ToList()
        };

        var versions = new List<Note>();
        Note? previous = null;
        void Save(List<NoteSection> sections, List<NoteSegment>? segments = null, List<string>? summary = null)
        {
            var update = NoteMutation.Prepare(session, new SectionUpdate(sections.Select(NoteTree.Clone).ToList(),
                NoteSections.CloneCitations(previous?.Citations ?? []), segments?.ToList(), summary));
            previous = NoteMutation.Create(session, previous, update, "input-" + versions.Count, previous?.Version ?? 0, "zh-Hant", "zh-Hant", "agent",
                new DateTime(2026, 10, 22, 1, 0, 0, DateTimeKind.Utc).AddMinutes(versions.Count));
            previous.Id = "note_" + versions.Count;
            versions.Add(previous);
        }

        var drafts = new List<NoteSection>();
        var segments = new List<NoteSegment>();
        var live = new List<NoteBlock>();
        for (var minute = 1; minute <= 165; minute++)
        {
            live.Add(Block($"live-{minute}", live.Count, minute, 150));
            Save([.. drafts, new() { Id = "live", Title = "Live", Order = 999, Blocks = live.ToList() }]);
            if (minute % 5 != 0) continue;
            var index = segments.Count;
            var blocks = Enumerable.Range(0, 4).Select(i => Block($"segment-{index}-{i}", i, minute - 4 + i, 400)).ToList();
            segments.Add(new() { Index = index, StartMs = (minute - 5) * 60_000L, EndMs = minute * 60_000L, InputHash = "window-" + index,
                Title = "Segment " + index, SourceIds = Enumerable.Range((minute - 5) * 8, 40).Select(Chunk).ToList(), Blocks = blocks,
                Concepts = [new() { Name = Text(index, 6), Definition = Text(index + 1, 80), SourceIds = [Chunk(minute * 8)] }],
                Corrections = [new() { Heard = Text(index + 2, 4), Written = Text(index + 3, 4), SourceIds = [Chunk(minute * 8 + 1)] }] });
            drafts.Add(new() { Id = "segment-" + index, Title = "Segment " + index, Order = index, Blocks = blocks.Select(NoteTree.Clone).ToList() });
            live.Clear();
            Save([.. drafts], segments);
        }
        // Stop: an outline of ten topics, then each polished in turn.
        var finished = Enumerable.Range(0, 10).Select(t => new NoteSection
        {
            Id = "topic-" + t, Title = "Topic " + t, Order = t,
            Blocks = drafts.Skip(t * 3).Take(3).SelectMany(x => x.Blocks).Select((x, i) => { var copy = NoteTree.Clone(x); copy.Order = i; return copy; }).ToList()
        }).ToList();
        Save(finished, segments, ["summary"]);
        foreach (var topic in finished)
        {
            foreach (var block in topic.Blocks) block.Body = Text(block.Order + 500, 380) + " " + block.Body[^270..];
            Save(finished);
        }

        long whole = 0, shared = 0;
        var parts = new Dictionary<string, BsonDocument>();
        var leftOut = 0;
        var addedPerPolish = new List<int>();
        foreach (var note in versions)
        {
            var original = note.ToBson();
            whole += original.Length;
            var stored = NoteParts.Store(note);
            Expect.That(NoteParts.HasParts(stored.Note), "Every three-speed version must be stored with shared parts");
            shared += stored.Note.ToBson().Length;
            var added = 0;
            foreach (var part in stored.Parts)
                if (parts.TryAdd(part["_id"].AsString, part)) { shared += part.ToBson().Length; added++; }
            if (note.Version > versions.Count - finished.Count) addedPerPolish.Add(added);
            if (!stored.Note.Contains("Markdown") && !stored.Note.Contains("TranscriptIds") && !stored.Note.Contains("Citations")) leftOut++;
            var read = NoteParts.Read(stored.Note, hash => parts[NoteParts.PartId(session, hash)]);
            Expect.That(read.ToBson().AsSpan().SequenceEqual(original), $"Version {note.Version} must read back exactly as it was saved");
        }
        Expect.That(leftOut == versions.Count, "Markdown, TranscriptIds and Citations must be worked out again for every three-speed version");
        Expect.That(addedPerPolish.All(x => x == 1), "Polishing one topic must add only that topic as a new part");
        Expect.That(shared * 10 < whole, $"Shared parts must store at least ten times less ({whole} vs {shared} bytes)");

        // A note saved before parts reads as it is; a note whose Markdown is its own keeps it.
        var legacy = versions[^1].ToBsonDocument();
        Expect.That(NoteParts.Read(legacy, _ => throw new InvalidOperationException()).ToBson().AsSpan().SequenceEqual(versions[^1].ToBson()),
            "A note saved whole must read as it was");
        var edited = versions[^1];
        edited.Markdown += "\n\nOwner text outside any topic";
        var own = NoteParts.Store(edited);
        Expect.That(own.Note.Contains("Markdown") && !own.Note.Contains("TranscriptIds") &&
            NoteParts.Read(own.Note, hash => own.Parts.Single(x => x["_id"].AsString == NoteParts.PartId(session, hash))).Markdown == edited.Markdown,
            "Markdown that the topics do not give back must be kept");
        Expect.Rejects(() => NoteParts.Read(own.Note, _ => throw new InvalidOperationException("missing")), "A missing part must fail the read");

        Console.WriteLine($"Note parts checks passed: {versions.Count} versions of a 165-minute lecture read back byte for byte; " +
            $"{whole / 1_000_000.0:F1} MB saved whole vs {shared / 1_000_000.0:F1} MB with {parts.Count} shared parts ({(double)whole / shared:F0}x less); " +
            "a polish adds one part; derived fields left out; whole and owner-Markdown notes kept");
    }
}
