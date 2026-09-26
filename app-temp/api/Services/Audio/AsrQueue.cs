using MongoDB.Driver;
using Playback.Api.Db;

namespace Playback.Api.Services.Audio;

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
    readonly bool paused = Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") == "yes";
    bool databaseUnavailable;

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
        foreach (var chunk in chunks.Where(x => x.Status is not ("transcribed" or "asr-empty" or "silent"))
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

    async Task ScanPending()
    {
        if (paused || Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes") return;
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
