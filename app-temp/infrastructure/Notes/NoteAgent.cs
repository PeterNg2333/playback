using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Providers;

namespace Playback.Api.Notes;

// Compatibility and worker host only; note use cases and decisions run in Application/Domain.
public sealed class NoteAgent : BackgroundService, IAsyncDisposable
{
    readonly PlaybackStore store;
    readonly NoteWorkflow workflow;
    readonly ILogger<NoteAgent> logger;
    readonly TimeProvider clock;
    readonly CancellationTokenSource stopping = new();
    int disposed;
    DateTimeOffset nextOrganization;

    public NoteAgent(PlaybackStore store, NoteWorkflow workflow, ILogger<NoteAgent> logger, TimeProvider? clock = null)
    {
        this.store = store;
        this.workflow = workflow;
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
    }

    // Existing checks and callers can still supply in-memory adapters through this constructor.
    public NoteAgent(PlaybackStore store, GeminiLanguageModel gemini, JevNoteGate jev, ILogger<NoteAgent> logger,
        AiActivity activity, TimeProvider? clock = null)
        : this(store, new NoteWorkflow(new NoteStoreAdapter(store), new NoteStoreAdapter(store), new GeminiNoteWriter(gemini),
            jev, new NoteActivityAdapter(activity, logger), clock, new NoteStoreAdapter(store), new NoteStoreAdapter(store), new NoteStoreAdapter(store), new NoteStoreAdapter(store)), logger, clock) { }

    public Task<object> Generate(string id, CancellationToken ct) => workflow.Generate(id, ct);
    public Task<object> Organize(string id, string sectionId, int baseVersion, CancellationToken ct) => workflow.Organize(id, sectionId, baseVersion, ct);
    public Task<object> RepairCoverage(string id, int baseVersion, CancellationToken ct) => workflow.RepairCoverage(id, baseVersion, ct);
    public void Tick(IEnumerable<string> ids, CancellationToken ct)
    {
        workflow.Tick(ids, ct);
    }
    public Task Drain() => workflow.Drain();
    public Task<bool> UpdateEvidence(string id, Func<Task<bool>> save, CancellationToken ct) => workflow.UpdateEvidence(id, save, ct);
    public Task<Playback.Application.Notes.NoteRebuildResult> Rebuild(string id, CancellationToken ct) => workflow.Rebuild(id, ct);
    public Task<int> WritePending(string id, CancellationToken ct) => workflow.WritePending(id, ct);
    public static void ValidateReferences(string markdown, IEnumerable<TermInsight> allowed) => NoteReferences.ValidateReferences(markdown, allowed.Select(x => x.Id));
    public static void ValidateSourceReferences(string markdown, IEnumerable<string> allowed) => NoteReferences.ValidateSourceReferences(markdown, allowed);
    public static string ReadableDraft(string json) => NoteWorkflow.ReadableDraft(json);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(stopping.Cancel);
        return ScanPending();
    }
    async Task ScanPending()
    {
        if (PlaybackEnvironment.Offline) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                try
                {
                    if (!PlaybackEnvironment.AutomaticNotes || !workflow.Due) continue;
                    var ids = await store.SessionsWithPendingNotes();
                    if (nextOrganization <= clock.GetUtcNow())
                    {
                        nextOrganization = clock.GetUtcNow().AddSeconds(5);
                        ids = ids.Concat(await store.SessionsNeedingOrganization()).Distinct().ToList();
                    }
                    Tick(ids.Where(PlaybackEnvironment.AllowsAutomaticWork), stopping.Token);
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
