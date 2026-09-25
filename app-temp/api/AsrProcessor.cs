using System.Threading.Channels;

public sealed class AsrProcessor : IAsyncDisposable
{
    readonly PlaybackStore store;
    readonly Providers providers;
    readonly ILogger<AsrProcessor> logger;
    readonly Channel<string> jobs = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    readonly HashSet<string> queued = [];
    readonly SemaphoreSlim serial = new(1, 1);
    readonly CancellationTokenSource stopping = new();
    readonly Task worker;

    public AsrProcessor(PlaybackStore store, Providers providers, ILogger<AsrProcessor> logger)
    {
        this.store = store;
        this.providers = providers;
        this.logger = logger;
        worker = Task.Run(ProcessQueue);
    }

    public int EnqueuePending(IEnumerable<ChunkRecord> chunks)
    {
        var count = 0;
        foreach (var chunk in chunks.Where(chunk => chunk.Status != "transcribed"))
            if (Enqueue(chunk.Id)) count++;
        return count;
    }

    public bool Enqueue(string id)
    {
        lock (queued)
        {
            if (!queued.Add(id)) return false;
            if (jobs.Writer.TryWrite(id)) return true;
            queued.Remove(id);
            throw new InvalidOperationException("ASR queue is stopping");
        }
    }

    async Task ProcessQueue()
    {
        try
        {
            await foreach (var id in jobs.Reader.ReadAllAsync(stopping.Token))
            {
                try { await Transcribe(id, stopping.Token); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogWarning(ex, "ASR failed for saved chunk {ChunkId}", id); }
                finally { lock (queued) queued.Remove(id); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    public async Task Transcribe(string id, CancellationToken ct)
    {
        await serial.WaitAsync(ct);
        try
        {
            var chunk = await store.Chunk(id) ?? throw new InvalidOperationException("Chunk not found");
            if (chunk.Status == "transcribed") return;
            if (await store.HasTranscript(id))
            {
                await store.SetChunkStatus(id, "transcribed");
                return;
            }
            await store.SetChunkStatus(id, "transcribing");
            var original = await providers.Transcribe(chunk.Path, ct);
            await store.SaveTranscript(chunk, original);
        }
        catch (Exception ex)
        {
            try
            {
                await store.SetChunkStatus(id, ct.IsCancellationRequested ? "pending-asr" : "asr-error",
                    ct.IsCancellationRequested ? null : ex.Message);
            }
            catch (Exception saveError) { logger.LogError(saveError, "Could not save ASR error for {ChunkId}", id); }
            throw;
        }
        finally { serial.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        jobs.Writer.TryComplete();
        await worker;
        stopping.Dispose();
        serial.Dispose();
    }
}
