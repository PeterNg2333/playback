using System.Diagnostics;
using System.Net.Sockets;
using Playback.Api.Db;
using Playback.Api.Services;

namespace Playback.Api.Services.Audio;

public sealed class AsrProcessor(
    PlaybackStore store,
    IAsrAdapter asr,
    ILogger<AsrProcessor> logger)
{
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
        if (chunk.Status is "transcribed" or "asr-empty" or "silent" or "vad-silence") return;
        if (await store.TranscriptForChunk(id) is { } existing)
        {
            await store.SetChunkStatus(id, existing.RecognitionStatus == "asr-empty" ? "asr-empty" : "transcribed");
            return;
        }
        if (AudioSilence.IsDigitalSilence(chunk.Path))
        {
            await store.SetChunkStatus(id, "silent");
            return;
        }
        await store.SetChunkStatus(id, "transcribing");
        var settings = await store.SessionSettings(chunk.SessionId);
        var watch = Stopwatch.StartNew();
        var speech = VadAudio.Prepare(chunk.Path, ct);
        if (!speech.HasSpeech)
        {
            await store.SetChunkStatus(id, "vad-silence");
            return;
        }
        var upload = Path.Combine(Path.GetTempPath(), $"playback-vad-{Guid.NewGuid():N}.wav");
        AsrResult result;
        try
        {
            await File.WriteAllBytesAsync(upload, speech.Wav, ct);
            result = await asr.Transcribe(new(upload, chunk.SessionId, chunk.SourceId, chunk.Sequence, chunk.Hash, settings.AsrLanguage, settings.AsrModel), ct);
        }
        finally { if (File.Exists(upload)) File.Delete(upload); }
        var model = result.Model ?? asr.Model;
        var display = LanguageSettings.CantoneseDisplay(result.Text, settings.AsrLanguage, result.Language);
        await store.SaveTranscript(chunk, result.Text, model, display == result.Text ? null : display,
            model.SupportsLanguageHint ? LanguageSettings.ProviderHint(settings.AsrLanguage) : null, result.Language);
        logger.LogInformation(
            "ASR {Provider}/{Model} completed for {ChunkId} in {ElapsedMs} ms; provider inference {InferenceSeconds} s, " +
            "duration {DurationSeconds} s, rtf {Rtf}, language {Language}",
            model.Provider, model.Model, id, watch.ElapsedMilliseconds, result.InferenceSeconds, result.DurationSeconds, result.RealTimeFactor, result.Language);
    }

    async Task SaveFailure(string id, CancellationToken ct, Exception failure)
    {
        try
        {
            if (ct.IsCancellationRequested) await store.SetChunkStatus(id, "pending-asr");
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
