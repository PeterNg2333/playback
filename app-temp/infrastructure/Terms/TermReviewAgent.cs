using System.Collections.Concurrent;
using Playback.Api.Db;
using Playback.Api.Providers;
using Playback.Api.Activity;

namespace Playback.Api.Terms;

public sealed class TermReviewAgent : BackgroundService, IAsyncDisposable
{
    readonly PlaybackStore store;
    readonly JevTermClassifier jev;
    readonly GeminiLanguageModel gemini;
    readonly ILogger<TermReviewAgent> logger;
    readonly TermReview workflow;
    readonly ConcurrentDictionary<string, DateTime> pendingSessions = new();
    readonly ConcurrentDictionary<string, int> attempts = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> reviewGates = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> explainGates = new();
    readonly CancellationTokenSource stopping = new();
    int disposed;

    public TermReviewAgent(PlaybackStore store, JevTermClassifier jev, GeminiLanguageModel gemini, ILogger<TermReviewAgent> logger, AiActivity activity, TermReview? workflow = null)
    {
        this.store = store;
        this.jev = jev;
        this.gemini = gemini;
        this.logger = logger;
        this.workflow = workflow ?? new(store, jev, gemini, new Playback.Infrastructure.Activity.WorkLog<TermReviewAgent>(logger), activity);
    }

    public static List<TermCandidate> Pending(SessionView session, int limit) => TermReview.Pending(session, limit);
    public Task<List<TermInsight>> Review(string id, int limit, CancellationToken ct) => workflow.Review(id, limit, ct);
    public Task<TermInsight> Explain(string sessionId, string insightId, CancellationToken ct, bool detail = false) => workflow.Explain(sessionId, insightId, ct, detail);

    void Queue(string id)
    {
        if (!PlaybackEnvironment.AllowsAutomaticWork(id)) return;
        if (attempts.GetValueOrDefault(id) >= 3) return;
        if (pendingSessions.Count < 128) pendingSessions.TryAdd(id, DateTime.UtcNow.AddSeconds(4));
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(stopping.Cancel);
        store.SourceChanged += Queue;
        try { await ScanPending(); }
        finally { store.SourceChanged -= Queue; }
    }
    async Task ScanPending()
    {
        if (PlaybackEnvironment.Offline) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var seeded = false;
        try
        {
            do
            {
                if (PlaybackEnvironment.AutomaticTerms && jev.IsConfigured)
                {
                    try
                    {
                        if (!seeded) {
                            foreach (var id in await store.TermReviewSessionIds()) Queue(id);
                            seeded = true;
                        }
                        foreach (var item in pendingSessions.Where(x => x.Value <= DateTime.UtcNow).Take(1))
                        {
                            pendingSessions.TryRemove(item.Key, out _);
                            try {
                                await Review(item.Key, 3, stopping.Token);
                                attempts.TryRemove(item.Key, out _);
                                var view = await store.Session(item.Key);
                                if (view is not null && (Pending(view, 1).Count > 0 || view.TermInsights.Any(x =>
                                    x.Highlight && x.Explanation is null && x.OutputLanguage == view.NoteLanguage))) Queue(item.Key);
                            }
                            catch {
                                if (attempts.AddOrUpdate(item.Key, 1, (_, value) => value + 1) < 3)
                                    pendingSessions[item.Key] = DateTime.UtcNow.AddSeconds(60);
                                throw;
                            }
                        }
                    }
                    catch (Exception ex) when (!stopping.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Automatic term review failed");
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(stopping.Token));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        store.SourceChanged -= Queue;
        stopping.Cancel();
        await StopAsync(CancellationToken.None);
        stopping.Dispose();
        base.Dispose();
    }
}
