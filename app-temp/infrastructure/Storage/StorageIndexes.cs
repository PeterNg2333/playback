using MongoDB.Driver;
using Playback.Application.Activity;
using Playback.Domain.Models;
namespace Playback.Api.Db;
public partial class PlaybackStore
{
    public async Task EnsureRecoveryIndexes(CancellationToken ct)
    {
        await Collection<Note>("notes").Indexes.CreateOneAsync(new CreateIndexModel<Note>(Builders<Note>.IndexKeys.Ascending(x => x.SessionId).Descending(x => x.Version), new CreateIndexOptions {Name = "note_session_version"}), cancellationToken: ct);
        await Collection<ChunkRecord>("chunks").Indexes.CreateOneAsync(new CreateIndexModel<ChunkRecord>(Builders<ChunkRecord>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.RecordedAt), new CreateIndexOptions {Name = "chunk_recovery"}), cancellationToken: ct);
        await Collection<ChunkRecord>("chunks").Indexes.CreateOneAsync(new CreateIndexModel<ChunkRecord>(Builders<ChunkRecord>.IndexKeys.Ascending(x => x.SessionId).Ascending(x => x.StartMs), new CreateIndexOptions {Name = "chunk_session_time"}), cancellationToken: ct);
        await Collection<Transcript>("transcripts").Indexes.CreateOneAsync(new CreateIndexModel<Transcript>(Builders<Transcript>.IndexKeys.Ascending(x => x.SessionId).Ascending(x => x.StartMs), new CreateIndexOptions {Name = "transcript_session_time"}), cancellationToken: ct);
        await Collection<Transcript>("transcripts").Indexes.CreateOneAsync(new CreateIndexModel<Transcript>(Builders<Transcript>.IndexKeys.Ascending(x => x.NoteStatus).Ascending(x => x.SessionId), new CreateIndexOptions {Name = "note_evidence_recovery"}), cancellationToken: ct);
    }
}
// Index creation is non-destructive; unavailable storage retries without reporting success.
public sealed class StorageIndexWorker(PlaybackStore store, ILogger<StorageIndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (PlaybackEnvironment.Offline) return;
        while (!ct.IsCancellationRequested)
        {
            try {await store.EnsureRecoveryIndexes(ct); return;}
            catch (Exception ex) when (!ct.IsCancellationRequested) {logger.LogWarning("Recovery indexes unavailable: {Error}", AiActivity.SafeError(ex));}
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
