using MongoDB.Driver;
using Playback.Application.Activity;
using Playback.Domain.Models;
using Playback.Infrastructure.Audio;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    readonly DecodedAudioCache decodedAudio = new(Path.Combine(Path.GetTempPath(), "playback-decoded-audio"));
    static readonly string[] CompressibleStatuses = [ChunkStatus.Transcribed, ChunkStatus.AsrEmpty, ChunkStatus.Silent, ChunkStatus.VadSilence];

    // Finished chunks recorded before `settledBefore` that are still WAV, oldest first. A chunk waiting for its second
    // ASR pass waits too, unless it is an hour old (the pass is off or has given up).
    public async Task<List<ChunkRecord>> CompressibleChunks(DateTime settledBefore, int limit)
    {
        var chunks = await Collection<ChunkRecord>("chunks")
            .Find(x => CompressibleStatuses.Contains(x.Status) && x.Storage == null && x.RecordedAt < settledBefore)
            .SortBy(x => x.RecordedAt).Limit(limit).ToListAsync();
        if (chunks.Count == 0) return chunks;
        var ids = chunks.Select(x => x.Id).ToList();
        var reviewing = (await Collection<Transcript>("transcripts").Find(x => ids.Contains(x.Id) && x.ReviewStatus == "pending")
            .Project(x => x.Id).ToListAsync()).ToHashSet();
        var given = DateTime.UtcNow.AddHours(-1);
        return chunks.Where(x => !reviewing.Contains(x.Id) || x.RecordedAt < given).ToList();
    }

    // Opus first, then the WAV goes, then the record says so: a crash between steps repeats a step and never loses audio.
    // A WAV Opus storage does not take, or a chunk without audio, is marked to stay as it is.
    public async Task<bool> CompressChunk(ChunkRecord chunk)
    {
        var wav = ChunkIdentity.WavPath(audioRoot, chunk.SessionId, chunk.SourceId, chunk.Sequence, chunk.Hash);
        var opus = Path.ChangeExtension(wav, ".opus");
        var compressed = File.Exists(opus) || File.Exists(wav) && OpusAudio.TryEncode(wav, opus);
        if (compressed && File.Exists(wav)) File.Delete(wav);
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == chunk.Id,
            Builders<ChunkRecord>.Update.Set(x => x.Storage, compressed ? "opus" : "wav"));
        return compressed;
    }

    public async Task<int> CompressSettledAudio(DateTime settledBefore, CancellationToken ct)
    {
        var done = 0;
        while (!ct.IsCancellationRequested && await CompressibleChunks(settledBefore, 100) is { Count: > 0 } batch)
        {
            var progressed = false;
            foreach (var chunk in batch)
            {
                if (ct.IsCancellationRequested) break;
                // A WAV still open elsewhere (Windows) is tried again on the next pass.
                try { if (await CompressChunk(chunk)) done++; progressed = true; }
                catch (IOException) { }
            }
            if (!progressed) break;
        }
        return done;
    }
}

// Finished chunks become Opus once ASR and its second pass are done (PlaybackEnvironment.CompressAudio).
public sealed class AudioCompressionWorker(PlaybackStore store, ILogger<AudioCompressionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (PlaybackEnvironment.Offline || !PlaybackEnvironment.CompressAudio) return;
        while (!ct.IsCancellationRequested)
        {
            try { await store.CompressSettledAudio(DateTime.UtcNow.AddMinutes(-2), ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogWarning("Audio compression paused: {Error}", AiActivity.SafeError(ex)); }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
