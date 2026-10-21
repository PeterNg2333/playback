using Playback.Application.Activity;
using Playback.Application.Notes;
using Playback.Infrastructure.Storage;

namespace Playback.Infrastructure.Notes;

// Hosts note writing in the background: once a second it hands the sessions with unwritten speech (or a pending Stop) to
// NoteWorkflow, which decides what each one needs. The note use cases themselves are in Application.
public sealed class NoteAgent(PlaybackStore store, NoteWorkflow workflow, ILogger<NoteAgent> logger) : BackgroundService, IAsyncDisposable
{
    readonly CancellationTokenSource stopping = new();
    int disposed;

    public Task<object> Generate(string id, CancellationToken ct) => workflow.Generate(id, ct);
    public Task Drain() => workflow.Drain();
    public Task<bool> UpdateEvidence(string id, Func<Task<bool>> save, CancellationToken ct) => workflow.UpdateEvidence(id, save, ct);
    public Task<NoteRebuildResult> Rebuild(string id, CancellationToken ct) => workflow.Rebuild(id, ct);
    public Task<int> WritePending(string id, CancellationToken ct) => workflow.WritePending(id, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (PlaybackEnvironment.Offline) return;
        stoppingToken.Register(stopping.Cancel);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                try
                {
                    if (!PlaybackEnvironment.AutomaticNotes || !workflow.Due) continue;
                    var ids = await store.SessionsWithPendingNotes();
                    workflow.Tick(ids.Where(PlaybackEnvironment.AllowsAutomaticWork), stopping.Token);
                }
                catch (Exception ex) when (!stopping.IsCancellationRequested)
                {
                    logger.LogWarning("Note scan failed: {Error}", AiActivity.SafeError(ex));
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping.Cancel();
        await StopAsync(CancellationToken.None);
        await workflow.Drain();
        stopping.Dispose();
        base.Dispose();
    }
}
