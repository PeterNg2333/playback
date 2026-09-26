using System.Diagnostics;
using Playback.Api.Db;

namespace Playback.Api.Services.Audio;

public sealed class AsrProcessor(
    PlaybackStore store,
    SenseVoiceClient senseVoice,
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
        var result = await senseVoice.Transcribe(chunk.Path, ct);
        await store.SaveTranscript(chunk, result.Text);
        logger.LogInformation(
            "ASR completed for {ChunkId} in {ElapsedMs} ms; provider inference {InferenceSeconds} s, " +
            "duration {DurationSeconds} s, rtf {Rtf}, language {Language}",
            id, watch.ElapsedMilliseconds, result.InferenceSeconds, result.DurationSeconds, result.RealTimeFactor, result.Language);
    }

    async Task SaveFailure(string id, CancellationToken ct, Exception failure)
    {
        try
        {
            await store.SetChunkStatus(id, ct.IsCancellationRequested ? "pending-asr" : "asr-error",
                ct.IsCancellationRequested ? null : failure.Message);
        }
        catch (Exception saveError)
        {
            logger.LogError(saveError, "Could not save ASR error for {ChunkId}", id);
        }
    }
}
