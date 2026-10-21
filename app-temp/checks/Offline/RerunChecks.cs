using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Audio.Asr;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Api.Sources;
using Playback.Application.Recording;
using Playback.Domain.Audio;

static class RerunChecks
{
    public static async Task Run()
    {
        Planner();
        Remap();
        await Workflow();
        Console.WriteLine("Rerun checks passed: windows close at pauses after the target and never pass 90 s, quiet edges trimmed, streams and earlier segments never split, " +
            "deterministic IDs; superseded citations, block sources and terms map to the segment; rerun supersedes rows with retained first pass, " +
            "the segment is written again with the corrected wording, failed windows keep the first pass, a restart resumes at the next window, recording refuses, " +
            "rebuild keeps locked blocks and history; lineage reaches block from chunk (fixture ASR and note model only)");
    }

    static ChunkRecord Chunk(string session, int index, long start, long end, string status = ChunkStatus.Transcribed, string stream = "s") => new()
    { Id = $"{session}-mic-{index}-h{index}", SessionId = session, SourceId = "mic", StreamId = stream, Sequence = index, Hash = "h" + index, StartMs = start, EndMs = end, Status = status, Path = "fixture" };

    static List<ChunkRecord> Chunks(string session, params int[] seconds)
    {
        long at = 0; var result = new List<ChunkRecord>();
        for (var i = 0; i < seconds.Length; i++) { result.Add(Chunk(session, i, at, at + seconds[i] * 1000L)); at += seconds[i] * 1000L; }
        return result;
    }

    static void Planner()
    {
        var chunks = Chunks("p", 8, 8, 8, 5, 8, 8, 8, 8, 8, 8, 8);
        var rows = chunks.Select(x => new Transcript { Id = x.Id, SessionId = "p", Original = "text" }).ToList();
        var windows = RerunWindows.Plan("job", chunks, rows, 20);
        Expect.That(windows.Count == 3 && windows[0].SpeechChunkIds.Count == 4 && windows[1].SpeechChunkIds.Count == 5 && windows[2].SpeechChunkIds.Count == 2 &&
            windows[0].ReplacesIds.SequenceEqual(chunks.Take(4).Select(x => x.Id)), "A window must close at the first pause after the target length, or 20 s later without one");
        Expect.That(windows.All(x => x.Id.StartsWith("p-window-") && x.AudioId.StartsWith("rerun_")) &&
            RerunWindows.Plan("job", chunks, rows, 20).Select(x => x.Id).SequenceEqual(windows.Select(x => x.Id)) &&
            !RerunWindows.Plan("other", chunks, rows, 20).Select(x => x.Id).Intersect(windows.Select(x => x.Id)).Any(), "Window identity must be deterministic per job");
        var long15 = Chunks("p", Enumerable.Repeat(8, 15).ToArray());
        var capped = RerunWindows.Plan("job", long15, [], 60);
        Expect.That(capped.All(x => x.EndMs - x.StartMs <= RerunWindows.MaxSeconds * 1000L) && capped.Sum(x => x.SpeechChunkIds.Count) == 15 &&
            capped.All(x => x.ReplacesIds.Count == 0), "Windows without a pause must stop at the 90 s cap and cover every chunk");
        var quiet = Chunks("p", 8, 8, 8, 8, 8);
        quiet[0].Status = ChunkStatus.Silent; quiet[2].Status = ChunkStatus.VadSilence; quiet[4].Status = ChunkStatus.Silent;
        var trimmed = RerunWindows.Plan("job", quiet, [], 20).Single();
        Expect.That(trimmed.ChunkIds.SequenceEqual(new[] { quiet[1].Id, quiet[2].Id, quiet[3].Id }) && trimmed.SpeechChunkIds.SequenceEqual(new[] { quiet[1].Id, quiet[3].Id }) &&
            trimmed.StartMs == quiet[1].StartMs && trimmed.EndMs == quiet[3].EndMs, "Quiet edges must be trimmed and interior quiet audio kept for continuity");
        var streams = Chunks("p", 8, 8, 8, 8); streams[2].StreamId = "t"; streams[3].StreamId = "t";
        Expect.That(RerunWindows.Plan("job", streams, [], 20).All(x => x.ChunkIds.Select(id => streams.First(c => c.Id == id).StreamId).Distinct().Count() == 1),
            "A window must never mix capture streams");
        var pending = Chunks("p", 8, 8, 8, 8); pending[2].Status = ChunkStatus.PendingAsr;
        Expect.That(RerunWindows.Plan("job", pending, [], 20).All(x => !x.ChunkIds.Contains(pending[2].Id)), "A chunk still waiting for ASR must not be rerun");
        // An earlier window W covers chunks 2–3; a pause after chunk 2 must not split it.
        var units = Chunks("p", 8, 8, 5, 8, 8, 8);
        var earlier = new Transcript { Id = "p-window-old", SessionId = "p", Kind = "window", ChunkIds = [units[2].Id, units[3].Id], Original = "w" };
        var unitRows = new List<Transcript> { new() { Id = units[0].Id }, new() { Id = units[1].Id }, earlier, new() { Id = units[4].Id }, new() { Id = units[5].Id } };
        var planned = RerunWindows.Plan("job", units, unitRows, 20);
        Expect.That(planned.Count(x => x.ReplacesIds.Contains(earlier.Id)) == 1 &&
            planned.Single(x => x.ReplacesIds.Contains(earlier.Id)).SpeechChunkIds.Contains(units[3].Id), "An earlier window segment must never be split");
    }

    static void Remap()
    {
        var note = new Playback.Domain.Notes.Note
        {
            SessionId = "r",
            Citations = [new() { Id = "cite_1", SourceIds = ["c1", "c2"] }],
            Sections = [new() { Id = "t", Title = "Topic", Blocks = [new() { Id = "b", Body = "Point [cite_1] and [c3]", SourceIds = ["c1", "c3"] }] }],
            TranscriptIds = ["c1", "c2", "c3"]
        };
        var map = Lineage.Supersessions([new Transcript { Id = "c1", SupersededBy = "w" }, new Transcript { Id = "c2", SupersededBy = "w" },
            new Transcript { Id = "c3", SupersededBy = "v" }, new Transcript { Id = "v", SupersededBy = "x" }]);
        Lineage.Apply(note, map);
        var block = note.Sections[0].Blocks[0];
        Expect.That(note.Citations[0].SourceIds.SequenceEqual(new[] { "w" }) && block.SourceIds.SequenceEqual(new[] { "w", "x" }) &&
            block.Body == "Point [cite_1] and [x]" && note.TranscriptIds.SequenceEqual(new[] { "w", "x" }),
            "Lineage must map citations, block sources and raw IDs through chains");
    }

    static async Task Workflow()
    {
        // A first-pass lecture with a recognition error, notes written from it, then a rerun.
        var store = new MemoryStore(); const string id = "rerun"; store.Add(id);
        var chunks = Chunks(id, 8, 8, 8, 8);
        string[] firstPass = ["The cash holds", "recently used data", "close to the", "processor."];
        for (var i = 0; i < chunks.Count; i++)
        {
            store.Chunks[id].Add(chunks[i]);
            store.Transcripts[id].Add(new Transcript { Id = chunks[i].Id, SessionId = id, SourceId = "mic", StartMs = chunks[i].StartMs, EndMs = chunks[i].EndMs, Original = firstPass[i] });
        }
        var model = new FixtureNoteModel();
        await using var agent = model.Workflow(store);
        await agent.WritePending(id, default);
        var before = store.Notes[id];
        Expect.That(before.Markdown.Contains("cash"), "Fixture notes were not written from the first pass");

        var repository = new MemoryReruns(store); var asr = new Recognizer(); var audio = new Audio(); var files = new Files();
        var recording = false;
        SessionRerun Rerun() => new(repository, asr, audio, files, new AiActivity(store), _ => recording, agent.UpdateEvidence,
            async (session, ct) => { await agent.WritePending(session, ct); }, async (session, ct) => { await agent.Rebuild(session, ct); });
        recording = true;
        await Expect.RejectsAsync(() => Rerun().Start(id, new(WindowSeconds: 20)), "A rerun must be refused while recording");
        recording = false;
        var job = await Rerun().Start(id, new(WindowSeconds: 20));
        await Expect.RejectsAsync(() => Rerun().Start(id, new(WindowSeconds: 20)), "A second rerun must wait for the first");
        Expect.That(job.Windows.Count == 1 && job.Windows[0].ReplacesIds.Count == 4, "Four contiguous chunks must form one 20 s+ window");
        await Rerun().Run(job, default);
        var window = job.Windows[0];
        var rows = store.Transcripts[id];
        var segment = rows.Single(x => x.Id == window.Id);
        var session = (await store.Session(id))!;
        var after = store.Notes[id];
        var current = session.CurrentNote!;
        Expect.That(job.Status == "completed" && job.NotesStatus == "completed" && window.Status == "completed" &&
            rows.Where(x => x.Id != window.Id).All(x => x.SupersededBy == window.Id) && segment.Active && segment.Kind == "window" &&
            segment.CoveredChunkIds.SequenceEqual(chunks.Select(x => x.Id)) && segment.FirstPassOriginal!.Contains("cash") &&
            session.Transcripts.Single().Id == window.Id && session.Supersessions.Count == 4,
            "Rerun must supersede every first-pass row with one active segment that keeps the first-pass text");
        Expect.That(asr.Requests.Single() is { Pass: var pass, Hash: var hash } && pass == "rerun-" + job.Id && hash == window.AudioId &&
            files.Windows.Single() == window.Id && audio.Disposed == 1, "Rerun must upload one combined window with a stable identity and dispose the prepared audio");
        var blockId = current.Sections.SelectMany(x => x.Blocks).First(b => b.SourceIds.Contains(window.Id)).Id;
        Expect.That(after.Markdown.Contains("cache holds") && !after.Markdown.Contains("cash") && segment.NoteStatus == "completed" &&
            current.Citations.SelectMany(x => x.SourceIds).All(x => x == window.Id),
            "The rewritten segment must carry the corrected wording, and its citations must name the new segment");
        Expect.That(store.Activity.Values.Count(x => x.Task == "ASR rerun window" && x.Status == "completed") == 1 &&
            store.Activity.Values.Any(x => x.Task == "Session rerun" && x.Status == "completed"), "Rerun activity must record the actual window call");

        var graph = LineageGraph.Build(session.Chunks, rows, current, [], chunks[0].Id, 3);
        Expect.That(graph.Edges.Any(x => x.Kind == "covers" && x.From == window.Id && x.To == chunks[0].Id) &&
            graph.Edges.Any(x => x.Kind == "supersedes" && x.From == window.Id) &&
            graph.Edges.Any(x => x.Kind == "cites" && x.To == window.Id && x.From == blockId) && graph.Nodes.Any(x => x.Id == blockId),
            "Lineage must trace chunk → segment → block");

        // A failing provider keeps the first pass; the window is retried and fails after three attempts.
        var failing = new MemoryStore(); failing.Add("fail");
        foreach (var chunk in Chunks("fail", 8, 8, 8)) { failing.Chunks["fail"].Add(chunk);
            failing.Transcripts["fail"].Add(new Transcript { Id = chunk.Id, SessionId = "fail", SourceId = "mic", StartMs = chunk.StartMs, EndMs = chunk.EndMs, Original = "first" }); }
        var failRepository = new MemoryReruns(failing); var broken = new Recognizer { Fail = true };
        var failRerun = new SessionRerun(failRepository, broken, audio, files, new AiActivity(failing), _ => false, (_, save, _) => save(),
            (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        var failJob = await failRerun.Start("fail", new(WindowSeconds: 20, Notes: "none"));
        await failRerun.Run(failJob, default);
        Expect.That(failJob.Status == "running" && failJob.Windows[0].Status == "pending" && failJob.Windows[0].Attempts == 1, "A failed window must stay pending for retry");
        await failRerun.RunPending(default); await failRerun.RunPending(default);
        Expect.That(failJob.Status == "failed" && failJob.Windows[0].Status == "failed" && broken.Requests.Count == 3 &&
            failing.Transcripts["fail"].All(x => x.Active && x.Original == "first"), "A window failing three times must keep every first-pass row");

        // Resume: a window completed before a restart is not uploaded again.
        var resume = new MemoryStore(); resume.Add("resume");
        foreach (var chunk in Chunks("resume", 8, 8, 5, 8, 8, 8)) { resume.Chunks["resume"].Add(chunk);
            resume.Transcripts["resume"].Add(new Transcript { Id = chunk.Id, SessionId = "resume", SourceId = "mic", StartMs = chunk.StartMs, EndMs = chunk.EndMs, Original = "first" }); }
        var resumeAsr = new Recognizer();
        var resumeRerun = new SessionRerun(new MemoryReruns(resume), resumeAsr, audio, files, new AiActivity(resume), _ => false, (_, save, _) => save(),
            (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        var resumeJob = await resumeRerun.Start("resume", new(WindowSeconds: 20, Notes: "none"));
        resumeJob.Windows[0].Status = "completed";
        await resumeRerun.RunPending(default);
        Expect.That(resumeJob.Windows.Count == 2 && resumeAsr.Requests.Count == 1 && resumeJob.Status == "completed", "A restart must resume at the next pending window");

        // Rebuild: a locked block survives; every source is written again; earlier versions stay in history.
        var locked = store.Notes[id].Sections.SelectMany(x => x.Blocks).First(); locked.UserEdited = true;
        var versionsBefore = store.Versions[id].Count;
        var rebuild = await Rerun().Start(id, new(Asr: false, Notes: "rebuild"));
        await Rerun().Run(rebuild, default);
        var rebuilt = store.Notes[id];
        Expect.That(rebuild.Status == "completed" && rebuild.NotesStatus == "completed" && store.Versions[id].Count >= versionsBefore + 2 &&
            rebuilt.Sections.SelectMany(x => x.Blocks).Any(x => x.Id == locked.Id && x.UserEdited) &&
            store.Versions[id].Any(x => x.Author == "user rebuild") && store.Transcripts[id].Where(x => x.Active).All(x => x.NoteStatus == "completed"),
            "Rebuild must keep locked blocks, write every active source again and keep history");
    }

    sealed class MemoryReruns(MemoryStore store) : IRerunRepository
    {
        readonly List<RerunJob> jobs = [];
        public Task<RerunJob?> Latest(string sessionId) => Task.FromResult(jobs.LastOrDefault(x => x.SessionId == sessionId));
        public Task<List<RerunJob>> Unfinished() => Task.FromResult(jobs.Where(x => !x.Finished).ToList());
        public Task Save(RerunJob job) { if (!jobs.Contains(job)) jobs.Add(job); return Task.CompletedTask; }
        public Task<SessionRecord> Settings(string sessionId) => Task.FromResult(new SessionRecord { Id = sessionId, AsrLanguage = "en" });
        public Task<List<ChunkRecord>> Chunks(string sessionId) => Task.FromResult(store.Chunks[sessionId].ToList());
        public Task<List<Transcript>> Transcripts(string sessionId) => Task.FromResult(store.Transcripts[sessionId].ToList());
        public Task<bool> Supersede(RerunWindow window, Transcript segment)
        {
            var rows = store.Transcripts[segment.SessionId];
            var replaced = rows.Where(x => window.ReplacesIds.Contains(x.Id)).ToList();
            if (replaced.Count != window.ReplacesIds.Count || replaced.Any(x => !x.Active)) return Task.FromResult(false);
            foreach (var row in replaced) row.SupersededBy = segment.Id;
            rows.Add(segment);
            return Task.FromResult(true);
        }
    }
    sealed class Recognizer : IAsrAdapter
    {
        public List<AsrRequest> Requests = []; public bool Fail; public AsrModel Model => new("fixture", "rerun", "rest");
        public Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Fail) throw new HttpRequestException("Fixture unavailable");
            return Task.FromResult(new AsrResult("The cache holds recently used data close to the processor.", null, null, "en", null, Model));
        }
    }
    sealed class Audio : ITranscriptionAudio
    {
        public int Disposed; public bool IsDigitalSilence(string path) => false;
        public string Display(string original, string requested, string? detected) => original;
        public Task<IPreparedSpeech> Prepare(string path, CancellationToken ct) => Task.FromResult<IPreparedSpeech>(new Speech(this));
        sealed class Speech(Audio owner) : IPreparedSpeech { public string Path => "fixture"; public int Bytes => 100; public bool HasSpeech => true; public void Dispose() => owner.Disposed++; }
    }
    sealed class Files : IRerunAudio
    {
        public List<string> Windows = [];
        public Task<string> Combine(string sessionId, RerunWindow window, CancellationToken ct) { Windows.Add(window.Id); return Task.FromResult("fixture.wav"); }
    }
}
