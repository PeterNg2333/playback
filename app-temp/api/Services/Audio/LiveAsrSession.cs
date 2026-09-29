using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Channels;
using NAudio.Wave;
using Playback.Api.Db;
using Playback.Api.Services.Ai;
using System.Text.Json;

namespace Playback.Api.Services.Audio;

// Ephemeral hypotheses only: saved WAVs and AsrProcessor remain the recovery/finalization boundary.
public sealed class LiveAsrSession : IAsyncDisposable
{
    public const int ChunkMilliseconds = 8_000;
    public const int PreviewMilliseconds = 2_000;
    public const string PreviewPromptVersion = "asr-interim-v1";
    public const string PreviewInstructions = "Transcription adapter parameters; no system prompt is sent to the transcription provider. Current-window hypotheses are ephemeral, not confirmed evidence; saved audio is finalized separately.";
    readonly object gate = new();
    readonly IAsrAdapter adapter;
    readonly AiActivity? activity;
    readonly string sessionId, sourceId;
    readonly Func<Task<SessionRecord>> settings;
    readonly MemoryStream pcm = new();
    readonly Queue<byte> preRoll = new();
    readonly Channel<byte[]> input = Channel.CreateBounded<byte[]>(32);
    readonly CancellationTokenSource cancel = new();
    readonly List<ActiveCaptureSegment> hypotheses = [];
    readonly long originMs;
    readonly DateTime originRecordedAt = DateTime.UtcNow;
    readonly Task streamTask;
    Task previewTask = Task.CompletedTask;
    long startMs, lastPreviewMs;
    int rate, phase, count;
    float sum;
    bool closed, streamFailed;
    bool utterance;
    int quietBytes;
    long speechRevision, lastPreviewRevision;
    string? error;

    public LiveAsrSession(IAsrAdapter adapter, string sessionId, string sourceId, long startMs,
        Func<Task<SessionRecord>> settings, AiActivity? activity = null)
    {
        this.adapter = adapter; this.sessionId = sessionId; this.sourceId = sourceId;
        this.startMs = originMs = lastPreviewMs = startMs; this.settings = settings;
        this.activity = activity;
        streamTask = adapter is IStreamingAsrAdapter streaming ? Task.Run(() => RunStream(streaming)) : Task.CompletedTask;
    }

    public string? Error { get { lock (gate) return error; } }
    public ActiveCaptureSegment[] Segments { get { lock (gate) return hypotheses.ToArray(); } }

    // Called from the device callback. Never waits for network, and memory is bounded.
    public void Feed(ReadOnlySpan<float> mono, int sampleRate, bool speech = true)
    {
        lock (gate)
        {
            if (closed) return;
            if (sampleRate < 8_000 || sampleRate > 96_000) throw new NotSupportedException("Unsupported live ASR sample rate");
            if (rate != sampleRate) { rate = sampleRate; phase = count = 0; sum = 0; }
            using var block = new MemoryStream();
            Span<byte> value = stackalloc byte[2];
            foreach (var sample in mono)
            {
                sum += float.IsFinite(sample) ? sample : 0; count++; phase += 16_000;
                while (phase >= rate)
                {
                    phase -= rate;
                    BinaryPrimitives.WriteInt16LittleEndian(value, (short)Math.Round(Math.Clamp(sum / count, -1f, 1f) * 32767));
                    block.Write(value);
                }
                if (phase < 16_000) { sum = 0; count = 0; }
            }
            var bytes = block.ToArray();
            if (bytes.Length == 0) return;
            if (speech)
            {
                speechRevision++;
                quietBytes = 0;
                if (!utterance) { pcm.Write(preRoll.ToArray()); preRoll.Clear(); utterance = true; }
            }
            else quietBytes += bytes.Length;
            if (!utterance)
            {
                foreach (var valueByte in bytes) preRoll.Enqueue(valueByte);
                while (preRoll.Count > 9600) preRoll.Dequeue(); // 300 ms onset context
                return;
            }
            if ((speech || quietBytes <= 22400) && pcm.Length + bytes.Length <= 32_000 * (ChunkMilliseconds / 1000 + 2)) pcm.Write(bytes);
            if (adapter is IStreamingAsrAdapter && !streamFailed && !input.Writer.TryWrite(bytes))
            {
                streamFailed = true; error = "Live ASR fell behind; using short audio requests";
                input.Writer.TryComplete(new InvalidOperationException(error));
            }
        }
    }

    public void Tick(long endMs, bool hasSpeech)
    {
        lock (gate)
        {
            if (closed || !hasSpeech || speechRevision == lastPreviewRevision || endMs - lastPreviewMs < PreviewMilliseconds || !previewTask.IsCompleted ||
                adapter is IStreamingAsrAdapter && !streamFailed || pcm.Length == 0) return;
            lastPreviewMs = endMs;
            lastPreviewRevision = speechRevision;
            var audio = pcm.ToArray(); var from = startMs;
            previewTask = Task.Run(() => Preview(audio, from, endMs));
        }
    }

    async Task Preview(byte[] audio, long from, long through)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var path = Path.Combine(Path.GetTempPath(), $"playback-interim-{Guid.NewGuid():N}.wav");
        ActivityRecord? call = null;
        try
        {
            var language = await settings();
            using (var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1))) writer.Write(audio);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var hash = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();
            if (activity is not null) {
                call = await activity.Begin(sessionId, "ASR interim preview", adapter.Model.Provider, language.AsrModel ?? adapter.Model.Model);
                activity.Context(call, PreviewPromptVersion, PreviewInstructions,
                    JsonSerializer.Serialize(new { sessionId, sourceId, fromMs = from, throughMs = through, pcmHash = hash, audioBytes = audio.Length,
                        language = language.AsrLanguage, model = language.AsrModel ?? adapter.Model.Model }));
                await activity.Start(call);
            }
            var providerWatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await adapter.Transcribe(new(path, sessionId, sourceId, from, hash, language.AsrLanguage, language.AsrModel), deadline.Token);
            if (call is not null) { call.ProviderLatencyMs = providerWatch.ElapsedMilliseconds; call.UsageJson = result.UsageJson;
                await activity!.End(call, "completed", "Ephemeral preview received; language/version guards decide whether it can be displayed", result.Model?.Model); }
            var text = LanguageSettings.CantoneseDisplay(result.Text, language.AsrLanguage, result.Language);
            var current = await settings();
            lock (gate)
            {
                if (closed || current.AsrLanguage != language.AsrLanguage || current.AsrModel != language.AsrModel || string.IsNullOrWhiteSpace(text)) return;
                error = null;
                Put(new(sourceId, from, through, originRecordedAt.AddMilliseconds(from - originMs), false, text,
                    result.Text, clock.ElapsedMilliseconds, result.Model?.Model ?? language.AsrModel ?? adapter.Model.Model));
            }
        }
        catch (OperationCanceledException ex) { if (call is not null) await activity!.Fail(call, ex); lock (gate) if (!closed) error = "Interim ASR timed out; saved audio will still be transcribed"; }
        catch (Exception ex) { if (call is not null) await activity!.Fail(call, ex); lock (gate) if (!closed) error = "Interim ASR unavailable; saved audio will still be transcribed"; }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { lock (gate) error = "Interim audio cleanup failed"; }
            catch (UnauthorizedAccessException) { lock (gate) error = "Interim audio cleanup failed"; }
        }
    }

    async Task RunStream(IStreamingAsrAdapter streaming)
    {
        using var streamCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        var ct = streamCancel.Token;
        try
        {
            var language = await settings();
            await using var stream = await streaming.OpenStream(new(sessionId, sourceId, language.AsrLanguage, language.AsrModel), ct);
            var read = Task.Run(async () =>
            {
                await foreach (var update in stream.ReadUpdates(ct))
                {
                    if (update.EndMs <= update.StartMs || update.StartMs < 0 || string.IsNullOrWhiteSpace(update.Text)) continue;
                    var text = LanguageSettings.CantoneseDisplay(update.Text, language.AsrLanguage, null);
                    lock (gate) Put(new(sourceId, originMs + update.StartMs, originMs + update.EndMs,
                        originRecordedAt.AddMilliseconds(update.StartMs), false, text));
                }
            }, ct);
            var send = Task.Run(async () =>
            {
                long sequence = 0;
                await foreach (var bytes in input.Reader.ReadAllAsync(ct)) await stream.Send(bytes, sequence++, ct);
                await stream.Complete(ct);
            }, ct);
            try
            {
                var first = await Task.WhenAny(send, read);
                await first;
                if (first == read && !send.IsCompleted)
                    throw new InvalidOperationException("Streaming ASR closed before input completed");
                await send;
                await read.WaitAsync(TimeSpan.FromSeconds(3), ct);
            }
            finally
            {
                streamCancel.Cancel();
                try { await Task.WhenAll(send, read); } catch (Exception) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { lock (gate) { streamFailed = true; error = "Streaming ASR unavailable; using short audio requests"; } }
    }

    void Put(ActiveCaptureSegment segment)
    {
        hypotheses.RemoveAll(x => x.StartMs == segment.StartMs);
        hypotheses.Add(segment);
        if (hypotheses.Count > 24) hypotheses.RemoveAt(0);
    }

    public void Rotate(long nextStartMs)
    {
        lock (gate) { if (closed) return; startMs = lastPreviewMs = nextStartMs; pcm.SetLength(0); utterance = false; quietBytes = 0; preRoll.Clear(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) closed = true;
        input.Writer.TryComplete();
        try { await streamTask.WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { cancel.Cancel(); }
        cancel.Cancel();
        await streamTask;
        await previewTask;
        cancel.Dispose(); pcm.Dispose();
    }
}
