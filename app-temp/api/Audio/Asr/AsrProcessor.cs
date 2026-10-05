using Playback.Api.Audio.Vad;
using System.Diagnostics;
using System.Net.Sockets;
using Playback.Api.Db;
using Playback.Api.Activity;
using System.Text.Json;

namespace Playback.Api.Audio.Asr;

public sealed class AsrProcessor(
    PlaybackStore store,
    IAsrAdapter asr,
    ILogger<AsrProcessor> logger,
    AiActivity activity)
{
    public const string PromptVersion = "asr-adapter-v1";
    public const string Instructions = "Audio transcription adapter parameters; no lecture-note generation prompt.";
    public async Task Process(string id, CancellationToken ct)
    {
        try { await Transcribe(id, ct); }
        catch (Exception ex)
        {
            await SaveFailure(id, ct, ex);
            throw;
        }
    }

    async Task Transcribe(string id, CancellationToken ct)
    {
        var chunk = await store.Chunk(id) ?? throw new InvalidOperationException("Chunk not found");
        if (ChunkStatus.Finished(chunk.Status)) return;
        if (await store.TranscriptForChunk(id) is { } existing)
        {
            await store.SetChunkStatus(id, existing.RecognitionStatus == "asr-empty" ? ChunkStatus.AsrEmpty : ChunkStatus.Transcribed);
            return;
        }
        if (AudioSilence.IsDigitalSilence(chunk.Path))
        {
            await store.SetChunkStatus(id, ChunkStatus.Silent);
            return;
        }
        await store.SetChunkStatus(id, ChunkStatus.Transcribing);
        var settings = await store.SessionSettings(chunk.SessionId);
        var watch = Stopwatch.StartNew();
        var speech = VadAudio.Prepare(chunk.Path, ct);
        if (!speech.HasSpeech)
        {
            await store.SetChunkStatus(id, ChunkStatus.VadSilence);
            return;
        }
        var upload = Path.Combine(Path.GetTempPath(), $"playback-vad-{Guid.NewGuid():N}.wav");
        var call = await activity.Begin(chunk.SessionId, "ASR", asr.Model.Provider, settings.AsrModel ?? asr.Model.Model, [id]);
        activity.Context(call, PromptVersion, Instructions,
            JsonSerializer.Serialize(new { chunk.SessionId, chunk.SourceId, chunk.Sequence, chunk.Hash, settings.AsrLanguage,
                requestedModel = settings.AsrModel ?? asr.Model.Model, audioBytes = speech.Wav.Length }));
        await activity.Start(call);
        AsrResult result;
        try
        {
            await File.WriteAllBytesAsync(upload, speech.Wav, ct);
            var providerWatch = Stopwatch.StartNew();
            result = await asr.Transcribe(new(upload, chunk.SessionId, chunk.SourceId, chunk.Sequence, chunk.Hash, settings.AsrLanguage, settings.AsrModel), ct);
            call.UsageJson = result.UsageJson;
            call.ProviderLatencyMs = providerWatch.ElapsedMilliseconds;
        }
        catch (Exception ex) { await activity.Fail(call, ex); throw; }
        finally { if (File.Exists(upload)) File.Delete(upload); }
        var model = result.Model ?? asr.Model;
        var display = LanguageSettings.CantoneseDisplay(result.Text, settings.AsrLanguage, result.Language);
        try {
            await store.SaveTranscript(chunk, result.Text, model, display == result.Text ? null : display,
                model.SupportsLanguageHint ? LanguageSettings.ProviderHint(settings.AsrLanguage) : null, result.Language);
            await activity.End(call, "completed", string.IsNullOrWhiteSpace(result.Text) ? "No words returned; original audio retained" : "Confirmed original ASR saved", model.Model);
        } catch (Exception ex) { await activity.Fail(call, ex); throw; }
        logger.LogInformation(
            "ASR {Provider}/{Model} completed for {ChunkId} in {ElapsedMs} ms; provider inference {InferenceSeconds} s, " +
            "duration {DurationSeconds} s, rtf {Rtf}, language {Language}",
            model.Provider, model.Model, id, watch.ElapsedMilliseconds, result.InferenceSeconds, result.DurationSeconds, result.RealTimeFactor, result.Language);
    }

    async Task SaveFailure(string id, CancellationToken ct, Exception failure)
    {
        try
        {
            if (ct.IsCancellationRequested) await store.SetChunkStatus(id, ChunkStatus.PendingAsr);
            else
            {
                var blocked = NetworkPermissionDenied(failure);
                await store.RecordAsrFailure(id,
                    blocked ? "ASR connection blocked by local network permissions; audio saved for manual retry" : failure.Message,
                    blocked);
            }
        }
        catch (Exception saveError)
        {
            logger.LogError(saveError, "Could not save ASR error for {ChunkId}", id);
        }
    }

    public static bool NetworkPermissionDenied(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SocketException socket && socket.SocketErrorCode == SocketError.AccessDenied)
                return true;
        return false;
    }
}
