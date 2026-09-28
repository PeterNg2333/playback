using System.Threading.Channels;
using NAudio.Wave;
using Playback.Api.Db;
using Playback.Api.Services.Audio;

internal static class LiveAsrCheck
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static async Task Wait(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!ready()) await Task.Delay(10, timeout.Token);
    }

    public static async Task Run()
    {
        var settings = new SessionRecord { AsrLanguage = "yue-en", AsrModel = "openai/whisper-large-v3-turbo" };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var rest = new Rest(async (request, ct) =>
        {
            Interlocked.Increment(ref calls);
            Require(request.SessionId == "session" && request.SourceId == "microphone" &&
                request.Language == "yue-en" && request.Model == settings.AsrModel, "Preview must use session, source, and selected settings");
            using var wav = new WaveFileReader(request.Path);
            Require(wav.WaveFormat.SampleRate == 16_000 && wav.WaveFormat.Channels == 1 && wav.Length == 64_000,
                "Interim request must contain real two-second normalized audio");
            await release.Task.WaitAsync(ct);
            return new("廣東話 and English", null, null, null, null);
        });
        await using (var live = new LiveAsrSession(rest, "session", "microphone", 0, () => Task.FromResult(settings)))
        {
            live.Feed(Enumerable.Repeat(0.1f, 96_000).ToArray(), 48_000);
            live.Tick(2000, false);
            Require(calls == 0, "Silence must not cause preview requests");
            live.Tick(2000, true);
            await Wait(() => calls == 1);
            live.Tick(4000, true); live.Tick(6000, true);
            Require(calls == 1, "Slow previews must be single flight, without a backlog");
            live.Rotate(8000);
            release.SetResult();
            await Wait(() => live.Segments.Length == 1);
            Require(live.Segments[0].StartMs == 0 && live.Segments[0].EndMs == 2000 &&
                live.Segments[0].InterimText == "廣東話 and English", "Late preview must retain its original range after rotation");
        }
        var streamed = new Streaming();
        await using (var live = new LiveAsrSession(streamed, "session", "system", 10000, () => Task.FromResult(settings)))
        {
            live.Feed([0.3f, 0.6f], 48_000);
            live.Feed([0.9f], 48_000);
            await Wait(() => streamed.Sent.Count == 1);
            Require(streamed.Request is { Language: "yue-en", SourceId: "system", Model: "openai/whisper-large-v3-turbo" },
                "Streaming must receive session language and model settings");
            Require(streamed.Sent[0].Sequence == 0 && BitConverter.ToInt16(streamed.Sent[0].Bytes) == (short)Math.Round(0.6f * 32767),
                "PCM resampling must retain phase and audio across device callbacks");
            streamed.Updates.Writer.TryWrite(new(0, 500, "intermediate", false));
            await Wait(() => live.Segments.Length == 1);
            Require(live.Segments[0].StartMs == 10000 && live.Segments[0].InterimText == "intermediate",
                "A genuine streaming hypothesis must appear before the two-second REST cadence");
            streamed.Updates.Writer.TryWrite(new(0, 900, "revised words", true));
            await Wait(() => live.Segments[0].InterimText == "revised words");
            Require(live.Segments.Length == 1, "Streaming revisions must replace, not duplicate, hypotheses");
        }
        Require(streamed.Completed && streamed.Disposed, "Stop must flush and dispose the provider stream");
        var fallbackCalls = 0;
        var failing = new Streaming { FailOpen = true, RestCall = (_, _) =>
        {
            Interlocked.Increment(ref fallbackCalls);
            return Task.FromResult(new AsrResult("fallback", null, null, null, null));
        } };
        await using (var live = new LiveAsrSession(failing, "session", "microphone", 0, () => Task.FromResult(settings)))
        {
            await Wait(() => live.Error is not null);
            live.Feed(new float[32000], 16000); live.Tick(2000, true);
            await Wait(() => live.Segments.Length == 1);
            Require(fallbackCalls == 1 && live.Segments[0].InterimText == "fallback", "A broken stream must fall back to REST without canceling capture");
        }
        Console.WriteLine("Live ASR checks passed (no network): real PCM previews, bounded single flight, rotation, streaming revisions, flush, failure fallback");
    }

    sealed class Rest(Func<AsrRequest, CancellationToken, Task<AsrResult>> call) : IAsrAdapter
    {
        public AsrModel Model => new("fixture", "rest", "rest");
        public Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct) => call(request, ct);
    }
    sealed class Streaming : IStreamingAsrAdapter, IAsrStream
    {
        public AsrModel Model => new("fixture", "stream", "streaming");
        public Channel<AsrStreamUpdate> Updates { get; } = Channel.CreateUnbounded<AsrStreamUpdate>();
        public List<(byte[] Bytes, long Sequence)> Sent { get; } = [];
        public AsrStreamRequest? Request;
        public bool Completed, Disposed, FailOpen;
        public Func<AsrRequest, CancellationToken, Task<AsrResult>>? RestCall;
        public Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct) =>
            RestCall?.Invoke(request, ct) ?? throw new Exception("A healthy streaming provider must not be polled for interim REST text");
        public Task<IAsrStream> OpenStream(AsrStreamRequest request, CancellationToken ct)
        {
            Request = request;
            return FailOpen ? Task.FromException<IAsrStream>(new IOException("fixture disconnect")) : Task.FromResult<IAsrStream>(this);
        }
        public ValueTask Send(ReadOnlyMemory<byte> bytes, long sequence, CancellationToken ct)
        { lock (Sent) Sent.Add((bytes.ToArray(), sequence)); return ValueTask.CompletedTask; }
        public IAsyncEnumerable<AsrStreamUpdate> ReadUpdates(CancellationToken ct) => Updates.Reader.ReadAllAsync(ct);
        public Task Complete(CancellationToken ct) { Completed = true; Updates.Writer.TryComplete(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
