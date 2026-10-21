using Playback.Application.Activity;
using Playback.Application.Assistance;
using Playback.Application.Recording;
using Playback.Domain.Audio;
using Playback.Domain.Models;
static class TranscriptionWorkflowChecks
{
    public static async Task Run()
    {
        var repository = new Sources(); var provider = new Recognizer(); var audio = new Audio(); var activityStore = new MemoryStore(); activityStore.Add("asr");
        var workflow = new Transcription(repository, provider, new Log(), audio, new AiActivity(activityStore));
        await workflow.Process("chunk", default); await workflow.Process("chunk", default);
        Expect.That(provider.Calls == 1 && repository.Saved?.Original == "Original [unclear] wording" && repository.ChunkValue.Status == ChunkStatus.Transcribed && audio.Disposals == 1, "Transcription lost raw identity, leaked preparation or repeated saved evidence");
        repository.Reset(); audio.Silent = true; await workflow.Process("chunk", default);
        Expect.That(provider.Calls == 1 && repository.ChunkValue.Status == ChunkStatus.Silent, "Digital silence reached the provider");
        repository.Reset(); audio.Silent = false; audio.Speech = false; await workflow.Process("chunk", default);
        Expect.That(provider.Calls == 1 && repository.ChunkValue.Status == ChunkStatus.VadSilence && audio.Disposals == 2, "VAD silence reached the provider or leaked preparation");
        repository.Reset(); audio.Speech = true; provider.Cancel = true; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try {await workflow.Process("chunk", cancel.Token); throw new CheckFailed("Cancelled provider appeared successful");} catch (OperationCanceledException) { }
        Expect.That(repository.Saved is null && repository.ChunkValue.Status == ChunkStatus.PendingAsr && audio.Disposals == 3, "Cancelled transcription lost durable retry or prepared-file disposal");
        Console.WriteLine("Transcription workflow checks passed: real application orchestration, immutable original/provider identity, duplicate recovery, digital/VAD silence, cancellation and prepared-audio disposal (fixture ports only)");
    }
    sealed class Sources : ITranscriptionRepository
    {
        public readonly ChunkRecord ChunkValue = new() {Id = "chunk", SessionId = "asr", SourceId = "microphone", Sequence = 12, Hash = new string('a', 64), Path = "fixture"}; public Transcript? Saved;
        public void Reset() {Saved = null; ChunkValue.Status = ChunkStatus.PendingAsr;}
        public Task<ChunkRecord?> Chunk(string id) => Task.FromResult<ChunkRecord?>(ChunkValue);
        public Task<Transcript?> TranscriptForChunk(string id) => Task.FromResult(Saved);
        public Task SetChunkStatus(string id, string status, string? error = null) {ChunkValue.Status = status; return Task.CompletedTask;}
        public Task<SessionRecord> SessionSettings(string id) => Task.FromResult(new SessionRecord {Id = "asr", AsrLanguage = "auto"});
        public Task SaveTranscript(ChunkRecord chunk, string original, AsrModel? asr = null, string? displayOriginal = null, string? languageHint = null, string? detectedLanguage = null) {Saved = new() {Id = chunk.Id, Original = original}; ChunkValue.Status = ChunkStatus.Transcribed; return Task.CompletedTask;}
        public Task<bool> RecordAsrFailure(string id, string error, bool stopRetries = false) => Task.FromResult(true);
    }
    sealed class Recognizer : IAsrAdapter
    {
        public int Calls; public bool Cancel; public AsrModel Model => new("fixture", "offline", "fixture");
        public Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct) {Calls++; Expect.That(request.SessionId == "asr" && request.SourceId == "microphone" && request.Sequence == 12 && request.Hash == new string('a', 64), "ASR retry identity changed at the application seam"); if (Cancel) throw new OperationCanceledException(ct); return Task.FromResult(new AsrResult("Original [unclear] wording", null, null, "en", null));}
    }
    sealed class Audio : ITranscriptionAudio
    {
        public bool Silent, Speech = true; public int Disposals;
        public bool IsDigitalSilence(string path) => Silent;
        public string Display(string original, string requested, string? detected) => original;
        public Task<IPreparedSpeech> Prepare(string path, CancellationToken ct) => Task.FromResult<IPreparedSpeech>(new Prepared(this, Speech));
        sealed class Prepared(Audio owner, bool speech) : IPreparedSpeech {public string Path => "fixture"; public int Bytes => 100; public bool HasSpeech => speech; public void Dispose() => owner.Disposals++;}
    }
    sealed class Log : IWorkLog {public void LogInformation(string message, params object?[] values) {} public void LogWarning(Exception ex, string message, params object?[] values) {} public void LogError(Exception ex, string message, params object?[] values) {}}
}
