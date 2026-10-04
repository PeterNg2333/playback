using MongoDB.Driver;
using Playback.Api.Db;

namespace Playback.Api.Audio.Asr;

public sealed class AsrQueue : IAsyncDisposable
{
    readonly PlaybackStore store;
    readonly AsrProcessor processor;
    readonly ILogger<AsrQueue> logger;
    readonly Queue<string> liveJobs = new();
    readonly Queue<string> recoveryJobs = new();
    readonly SemaphoreSlim available = new(0);
    readonly HashSet<string> queued = [];
    readonly Dictionary<string, (int Failures, DateTime RetryAt)> retries = [];
    readonly CancellationTokenSource stopping = new();
    readonly Task[] workers;
    readonly Task scanner;
    readonly bool paused = PlaybackEnvironment.ExternalAsrPaused;
    bool databaseUnavailable;

    public bool LiveBacklog { get { lock (queued) return liveJobs.Count > 0; } }

    public AsrQueue(PlaybackStore store, AsrProcessor processor, ILogger<AsrQueue> logger)
    {
        this.store = store;
        this.processor = processor;
        this.logger = logger;
        if (paused) logger.LogWarning("External ASR is paused; saved audio remains queued for a later restart");
        workers = Enumerable.Range(0, 2).Select(_ => Task.Run(ProcessQueue)).ToArray();
        scanner = Task.Run(ScanPending);
    }

    public int EnqueuePending(IEnumerable<ChunkRecord> chunks)
    {
        var count = 0;
        foreach (var chunk in chunks.Where(x => ChunkStatus.AwaitingTranscript(x.Status))
            .OrderByDescending(x => x.RecordedAt ?? DateTime.MinValue))
            if (Enqueue(chunk.Id, recovery: true)) count++;
        return count;
    }

    public bool Enqueue(string id, bool recovery = false)
    {
        if (paused) return false;
        lock (queued)
        {
            if (stopping.IsCancellationRequested) throw new InvalidOperationException("ASR queue is stopping");
            if (queued.Contains(id)) return false;
            if (retries.TryGetValue(id, out var retry) && retry.RetryAt > DateTime.UtcNow) return false;
            queued.Add(id);
            (recovery ? recoveryJobs : liveJobs).Enqueue(id);
            available.Release();
            return true;
        }
    }

    public async Task Retry(string sessionId, IEnumerable<string>? ids)
    {
        if (paused) throw new InvalidOperationException("External ASR is paused");
        if (ids is null) throw new InvalidOperationException("Choose audio chunks to retry");
        var unique = ids.Distinct().Take(101).ToArray();
        if (unique.Length is 0 or > 100) throw new InvalidOperationException("Choose 1–100 audio chunks to retry");
        foreach (var id in unique)
        {
            await store.RetryAsr(sessionId, id);
            lock (queued) retries.Remove(id);
            Enqueue(id);
        }
    }

    async Task ScanPending()
    {
        if (paused || PlaybackEnvironment.Offline
            || PlaybackEnvironment.ValidationApi
            || !PlaybackEnvironment.ResumeSavedAsr) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do { await ScanOnce(); }
            while (await timer.WaitForNextTickAsync(stopping.Token));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    async Task ScanOnce()
    {
        try
        {
            var migrated = await store.MigrateLegacyAsrStatuses();
            if (migrated.Awaiting > 0) logger.LogInformation("Queued {Count} legacy saved audio chunks", migrated.Awaiting);
            if (migrated.Failed > 0) logger.LogInformation("Marked {Count} legacy ASR failures for manual retry", migrated.Failed);
            var restored = EnqueuePending(await store.PendingAsrChunks());
            if (restored > 0) logger.LogInformation("Queued {Count} saved audio chunks for ASR", restored);
            databaseUnavailable = false;
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            if (!databaseUnavailable)
                logger.LogWarning("MongoDB is unavailable; saved ASR chunks will be scanned when it returns");
            databaseUnavailable = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not scan saved ASR chunks");
        }
    }

    async Task ProcessQueue()
    {
        try
        {
            while (true)
            {
                await available.WaitAsync(stopping.Token);
                string id;
                lock (queued) id = liveJobs.Count > 0 ? liveJobs.Dequeue() : recoveryJobs.Dequeue();
                if (!await ProcessJob(id)) break;
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    async Task<bool> ProcessJob(string id)
    {
        try
        {
            await processor.Process(id, stopping.Token);
            lock (queued) retries.Remove(id);
            return true;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            ChunkRecord? chunk = null;
            try { chunk = await store.Chunk(id); }
            catch (Exception lookupError)
            {
                logger.LogWarning(lookupError, "Could not check saved ASR status for {ChunkId}", id);
            }
            if (chunk?.Status == ChunkStatus.AsrManual)
            {
                logger.LogWarning("ASR stopped after {Attempts} attempts for saved chunk {ChunkId}; manual retry available", chunk.AsrAttempts, id);
                return true;
            }
            lock (queued)
            {
                var failures = retries.GetValueOrDefault(id).Failures + 1;
                var delaySeconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures - 1, 6)));
                retries[id] = (failures, DateTime.UtcNow.AddSeconds(delaySeconds));
            }
            logger.LogWarning(ex, "ASR failed for saved chunk {ChunkId}; automatic retry scheduled", id);
            return true;
        }
        finally { lock (queued) queued.Remove(id); }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        await Task.WhenAll(workers.Append(scanner));
        stopping.Dispose();
        available.Dispose();
    }
}
