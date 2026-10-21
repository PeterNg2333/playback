using Playback.Application.Activity;
using Playback.Application.Recording;
using Playback.Domain.Models;

static class TranscriptReviewChecks
{
    public static async Task Run()
    {
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        TranscriptReviewItem Item(int i, string source = "microphone", string stream = "stream") => new(
            new() {Id = "chunk-" + i, SessionId = "review", SourceId = source, StreamId = stream, Sequence = i, Hash = "hash-" + i,
                StartMs = i * 8000, EndMs = (i + 1) * 8000, Path = "fixture"},
            new() {Id = "chunk-" + i, SessionId = "review", Original = "First pass", ConfirmedAt = now, ReviewStatus = "pending"});
        var items = Enumerable.Range(0, 4).Select(i => Item(i)).ToList();
        Expect.That(TranscriptReview.Due(items.Take(3), now.AddSeconds(29)).Count == 0 &&
            TranscriptReview.Due(items.Take(1), now.AddSeconds(30)).Single().Count == 1 &&
            TranscriptReview.Due(items, now).Single().Count == 4, "Review must wait for 30 seconds or four chunks");
        Expect.That(TranscriptReview.Due([Item(0), Item(1), Item(2, "system"), Item(3, stream: "other")], now).Count == 0,
            "Review count must not mix sources or capture streams");
        var store = new ReviewStore(items); var asr = new Recognizer(); var audio = new Audio();
        var logs = new MemoryStore(); logs.Add("review"); var noteCalls = 0; var applies = 0;
        var workflow = new TranscriptReview(store, asr, audio, new AiActivity(logs), (_, _) =>
        { noteCalls++; Expect.That(store.Saved.Count == 4, "Notes ran before the batch corrections were saved"); return Task.CompletedTask; },
            (_, save, _) => {applies++; return save();});
        await workflow.Run(now, default); await workflow.Run(now.AddSeconds(40), default);
        Expect.That(asr.Requests.Count == 4 && store.Saved.Count == 4 && noteCalls == 1 && applies == 4 && audio.Disposed == 4 &&
            asr.Requests.All(x => x.Pass == "review-v1" && items.Any(i => i.Chunk.Id == "chunk-" + x.Sequence && i.Chunk.Hash == x.Hash)) &&
            logs.Activity.Values.Count(x => x.Task == "ASR re-transcription" && x.Status == "completed") == 4,
            "Review must retain identity, dispose preparation, log actual calls, rerun notes once and not re-upload completed reviews");
        store = new ReviewStore([Item(0)]); asr = new Recognizer {Fail = true}; noteCalls = 0;
        workflow = new(store, asr, audio, new AiActivity(logs), (_, _) => {noteCalls++; return Task.CompletedTask;}, (_, save, _) => save());
        await workflow.Run(now.AddSeconds(30), default);
        Expect.That(store.Saved.Count == 0 && store.Failures == 1 && noteCalls == 0 && store.Items[0].Transcript.Original == "First pass",
            "Failed review must retain the first pass and must not invent a successful note rerun");
        Console.WriteLine("Transcript review checks passed: age/count batching, source/stream isolation, stable review identity, one notes rerun per changed batch, durable completion reuse, real activity outcomes and failure retention (in-memory ports)");
    }
    sealed class ReviewStore(List<TranscriptReviewItem> items) : ITranscriptReviewRepository
    {
        public List<TranscriptReviewItem> Items = items; public List<AsrResult> Saved = []; public int Failures;
        public Task<List<TranscriptReviewItem>> Pending(DateTime now) => Task.FromResult(Items.Where(x => x.Transcript.ReviewStatus == "pending").ToList());
        public Task<SessionRecord> Settings(string id) => Task.FromResult(new SessionRecord {AsrLanguage = "en"});
        public Task<bool> Save(TranscriptReviewItem item, AsrResult result, string display) {Saved.Add(result); item.Transcript.ReviewStatus = "completed"; return Task.FromResult(true);}
        public Task Failed(TranscriptReviewItem item, string error, DateTime now) {Failures++; return Task.CompletedTask;}
    }
    sealed class Recognizer : IAsrAdapter
    {
        public List<AsrRequest> Requests = []; public bool Fail; public AsrModel Model => new("fixture", "review", "rest");
        public Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct)
        { Requests.Add(request); if (Fail) throw new HttpRequestException("Fixture unavailable"); return Task.FromResult(new AsrResult("Second pass", null, null, "en", null, Model)); }
    }
    sealed class Audio : ITranscriptionAudio
    {
        public int Disposed; public bool IsDigitalSilence(string path) => false;
        public string Display(string original, string requested, string? detected) => original;
        public Task<IPreparedSpeech> Prepare(string path, CancellationToken ct) => Task.FromResult<IPreparedSpeech>(new Speech(this));
        sealed class Speech(Audio owner) : IPreparedSpeech {public string Path => "fixture"; public int Bytes => 100; public bool HasSpeech => true; public void Dispose() => owner.Disposed++;}
    }
}
