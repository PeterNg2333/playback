using MongoDB.Driver;
using Playback.Application.Recording;
using Playback.Domain.Audio;
using Playback.Domain.Models;
using Playback.Infrastructure.Audio;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    public IRerunRepository Reruns() => new RerunRepository(this);
    public IRerunAudio RerunAudio() => new RerunWindowAudio(this);
    public virtual async Task<RerunView?> RerunStatus(string sessionId) => await Reruns().Latest(sessionId) is { } job ? RerunView.Of(job) : null;

    sealed class RerunRepository(PlaybackStore store) : IRerunRepository
    {
        IMongoCollection<RerunJob> Jobs => store.Collection<RerunJob>("rerun_jobs");
        public async Task<RerunJob?> Latest(string sessionId) =>
            await Jobs.Find(x => x.SessionId == sessionId).SortByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        public async Task<List<RerunJob>> Unfinished() =>
            (await Jobs.Find(x => x.Status == "running").SortBy(x => x.CreatedAt).Limit(16).ToListAsync())
                .Where(x => PlaybackEnvironment.AllowsAutomaticWork(x.SessionId)).ToList();
        public async Task Save(RerunJob job)
        {
            await Jobs.ReplaceOneAsync(x => x.Id == job.Id, job, new ReplaceOptions { IsUpsert = true });
            store.Touch(job.SessionId, "rerun", [job.SessionId]);
        }
        public Task<SessionRecord> Settings(string sessionId) => store.SessionSettings(sessionId);
        public async Task<List<ChunkRecord>> Chunks(string sessionId) =>
            await store.Collection<ChunkRecord>("chunks").Find(x => x.SessionId == sessionId).SortBy(x => x.StartMs).ToListAsync();
        public Task<List<Transcript>> Transcripts(string sessionId) => store.AllTranscripts(sessionId);

        // No transaction: the segment is written first (briefly overlapping), then rows are claimed only while
        // still active. A partial claim is undone together with the segment, so a reader never loses speech.
        public async Task<bool> Supersede(RerunWindow window, Transcript segment)
        {
            var rows = store.Collection<Transcript>("transcripts");
            var ids = window.ReplacesIds;
            await rows.ReplaceOneAsync(x => x.Id == segment.Id, segment, new ReplaceOptions { IsUpsert = true });
            var now = DateTime.UtcNow;
            var claimed = ids.Count == 0 ? 0 : (await rows.UpdateManyAsync(x => ids.Contains(x.Id) && x.SupersededBy == null && x.SessionId == segment.SessionId,
                Builders<Transcript>.Update.Set(x => x.SupersededBy, segment.Id).Set(x => x.SupersededAt, now))).ModifiedCount;
            if (claimed != ids.Count)
            {
                await rows.UpdateManyAsync(x => ids.Contains(x.Id) && x.SupersededBy == segment.Id,
                    Builders<Transcript>.Update.Set(x => x.SupersededBy, null).Set(x => x.SupersededAt, null));
                await rows.DeleteOneAsync(x => x.Id == segment.Id);
                return false;
            }
            // A failed or empty first pass is now covered by the segment's words.
            var speech = window.SpeechChunkIds;
            await store.Collection<ChunkRecord>("chunks").UpdateManyAsync(x => speech.Contains(x.Id) &&
                (x.Status == ChunkStatus.AsrError || x.Status == ChunkStatus.AsrManual || x.Status == ChunkStatus.AsrEmpty),
                Builders<ChunkRecord>.Update.Set(x => x.Status, ChunkStatus.Transcribed).Set(x => x.Error, null));
            store.Touch(segment.SessionId, "source", ids.Concat(speech).Append(segment.Id));
            store.Touch(segment.SessionId, "lineage", [segment.SessionId]);
            store.SourceChanged?.Invoke(segment.SessionId);
            return true;
        }
    }

    sealed class RerunWindowAudio(PlaybackStore store) : IRerunAudio
    {
        public async Task<string> Combine(string sessionId, RerunWindow window, CancellationToken ct)
        {
            var files = new WavCombinedAudioFiles(store.audioRoot, key => Task.FromResult(store.ChunkAudioPath(key)));
            var chunks = (await store.Collection<ChunkRecord>("chunks").Find(x => x.SessionId == sessionId && window.ChunkIds.Contains(x.Id)).ToListAsync())
                .ToDictionary(x => x.Id);
            var segment = new CombinedAudioSegment { Id = window.AudioId, SessionId = sessionId, SourceId = window.SourceId, StreamId = window.StreamId,
                PolicyRevision = "rerun-v1", StartMs = window.StartMs, EndMs = window.EndMs, RecordedAt = window.RecordedAt };
            long offset = 0;
            foreach (var id in window.ChunkIds)
            {
                var chunk = chunks.GetValueOrDefault(id) ?? throw new InvalidOperationException("Rerun window chunk is missing");
                var (format, frames) = await files.Inspect(id, ct);
                if (segment.Format is null) segment.Format = format;
                else if (segment.Format != format) throw new InvalidOperationException("Rerun window mixes audio formats");
                segment.Children.Add(new(id, chunk.Sequence, chunk.Hash, offset, frames, chunk.StartMs, chunk.EndMs));
                offset += frames;
            }
            // Deterministic path: a retried window rewrites the same file after re-verifying every original hash.
            await files.Publish(segment, ct);
            return files.PathFor(sessionId, segment.Id);
        }
    }
}
