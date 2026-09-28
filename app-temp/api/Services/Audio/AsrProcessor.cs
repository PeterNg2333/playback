using System.Diagnostics;
using System.Net.Sockets;
using Playback.Api.Db;

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
        if (chunk.Status is "transcribed" or "asr-empty" or "silent") return;
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
        var watch = Stopwatch.StartNew();
        var result = await asr.Transcribe(new(chunk.Path, chunk.SessionId, chunk.SourceId, chunk.Sequence, chunk.Hash), ct);
        await store.SaveTranscript(chunk, result.Text, asr.Model);
        logger.LogInformation(
            "ASR {Provider}/{Model} completed for {ChunkId} in {ElapsedMs} ms; provider inference {InferenceSeconds} s, " +
            "duration {DurationSeconds} s, rtf {Rtf}, language {Language}",
            asr.Model.Provider, asr.Model.Model, id, watch.ElapsedMilliseconds, result.InferenceSeconds, result.DurationSeconds, result.RealTimeFactor, result.Language);
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
