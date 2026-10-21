using MongoDB.Driver;
using Playback.Application.Audio;
using Playback.Domain.Audio;
using Playback.Domain.Models;
using Playback.Infrastructure.Audio;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    public event Action<string>? AudioChanged;
    public async Task SaveAudioStream(string sessionId, string streamId, bool closed, bool notify = true)
    {
        await Collection<AudioStreamRecord>("audio_streams").ReplaceOneAsync(x => x.Id == streamId,
            new() { Id = streamId, SessionId = sessionId, Closed = closed, LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1) }, new ReplaceOptions { IsUpsert = true });
        if (notify) AudioChanged?.Invoke(sessionId);
    }
    public virtual async Task<List<CombinedAudioSegment>> CombinedAudio(string id) => QuietAudio.Playback(await Collection<CombinedAudioSegment>("combined_audio")
        .Find(x => x.SessionId == id && x.State == "published").SortBy(x => x.StartMs).ToListAsync());
    public async Task<string?> CombinedAudioPath(string sessionId, string segmentId)
    {
        await RequireSessionOwner(sessionId);
        var item = await Collection<CombinedAudioSegment>("combined_audio").Find(x => x.Id == segmentId && x.SessionId == sessionId && x.State == "published").FirstOrDefaultAsync();
        if (item is null) return null;
        var path = new WavCombinedAudioFiles(audioRoot, _ => Task.FromResult<string?>(null)).PathFor(sessionId, item.Id);
        return File.Exists(path) ? path : null;
    }
    public async Task RebuildCombinedAudio(string id, CancellationToken ct)
    {
        var workflow = new CombinedAudioWorkflow(new CombinedRepository(this), new WavCombinedAudioFiles(audioRoot,
            key => Task.FromResult(ChunkAudioPath(key))));
        await workflow.Run(id, AudioPolicy.CombineQuiet, AudioPolicy.MaxCombinedSeconds,
            "quiet-v1:" + AudioPolicy.MaxCombinedSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
    }
    public async Task<List<string>> QuietAudioSessions() => (await Collection<ChunkRecord>("chunks")
        .Find(x => x.Status == ChunkStatus.Silent || x.Status == ChunkStatus.VadSilence).SortByDescending(x => x.RecordedAt)
        .Limit(4096).Project(x => x.SessionId).ToListAsync()).Distinct().ToList();
    sealed class CombinedRepository(PlaybackStore store) : ICombinedAudioRepository
    {
        public async Task<List<AudioPart>> Parts(string id)
        {
            var chunks = await store.Collection<ChunkRecord>("chunks").Find(x => x.SessionId == id).SortBy(x => x.StartMs).Limit(50001).ToListAsync();
            if (chunks.Count > 50000) throw new InvalidOperationException("Quiet audio scan exceeds the session chunk budget; originals remain available");
            var streams = await store.Collection<AudioStreamRecord>("audio_streams").Find(x => x.SessionId == id).ToListAsync();
            return chunks.Select(x => new AudioPart(x.Id, id, x.SourceId, x.StreamId ?? "legacy:" + id + ":" + x.SourceId,
                x.Sequence, x.Hash, x.StartMs, x.EndMs, x.Status, x.RecordedAt,
                x.StreamId is null || streams.Any(s => s.Id == x.StreamId && (s.Closed || s.LeaseExpiresAt is null || s.LeaseExpiresAt <= DateTime.UtcNow)))).ToList();
        }
        public Task<CombinedAudioSegment?> Find(string id) => store.Collection<CombinedAudioSegment>("combined_audio").Find(x => x.Id == id).FirstOrDefaultAsync()!;
        public async Task Save(CombinedAudioSegment item)
        {
            await store.Collection<CombinedAudioSegment>("combined_audio").ReplaceOneAsync(x => x.Id == item.Id, item, new ReplaceOptions { IsUpsert = true });
            if (item.State == "published") store.Touch(item.SessionId, "combined-audio");
        }
    }
}
