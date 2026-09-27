using System.Collections.Concurrent;
using Playback.Api.Db;
using Playback.Api.Services.Ai.Providers;
using Playback.Api.Terms;

namespace Playback.Api.Services.Ai.Agents;

public sealed class TermReviewAgent : IAsyncDisposable
{
    readonly PlaybackStore store;
    readonly JevTermClassifier jev;
    readonly GeminiLanguageModel gemini;
    readonly ILogger<TermReviewAgent> logger;
    readonly ConcurrentDictionary<string, SemaphoreSlim> explainGates = new();
    readonly CancellationTokenSource stopping = new();
    readonly Task scanner;

    public TermReviewAgent(PlaybackStore store, JevTermClassifier jev, GeminiLanguageModel gemini, ILogger<TermReviewAgent> logger)
    {
        this.store = store;
        this.jev = jev;
        this.gemini = gemini;
        this.logger = logger;
        scanner = Task.Run(ScanPending);
    }

    public static List<TermCandidate> Pending(SessionView session, int limit) => session.Terms
        .Where(candidate => !session.TermInsights.Any(insight =>
            insight.Id == PlaybackStore.TermInsightId(session.Id, candidate.Text)))
        .Take(limit).ToList();

    public async Task<List<TermInsight>> Review(string id, int limit, CancellationToken ct)
    {
        var session = await store.Session(id) ?? throw new InvalidOperationException("Session not found");
        var candidates = Pending(session, limit);
        foreach (var candidate in candidates)
        {
            var rank = (JevRankResult)await jev.Rank(candidate.Text, ct);
            await store.SaveTermRanking(id, candidate, rank);
        }
        logger.LogInformation("Term review completed: {SessionId}, {Count} candidates", id, candidates.Count);
        return (await store.Session(id))!.TermInsights;
    }

    public async Task<TermInsight> Explain(string sessionId, string insightId, CancellationToken ct)
    {
        var gate = explainGates.GetOrAdd(insightId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var insight = await store.TermInsight(sessionId, insightId)
                ?? throw new InvalidOperationException("Term insight not found");
            if (!insight.Highlight) throw new InvalidOperationException("This term was not selected for explanation");
            if (insight.Explanation is not null) return insight;
            var explanation = await gemini.GroundedExplain(insight.Term, ct);
            return await store.SaveTermExplanation(sessionId, insightId, explanation);
        }
        finally { gate.Release(); }
    }

    async Task ScanPending()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes") return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        try
        {
            do
            {
                if (Environment.GetEnvironmentVariable("PLAYBACK_AUTO_TERMS") == "yes" && jev.IsConfigured)
                {
                    try
                    {
                        var remaining = 3;
                        foreach (var session in await store.Sessions())
                        {
                            if (remaining == 0) break;
                            var view = await store.Session(session.Id);
                            var count = view is null ? 0 : Pending(view, remaining).Count;
                            if (count == 0) continue;
                            await Review(session.Id, count, stopping.Token);
                            remaining -= count;
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
        stopping.Cancel();
        await scanner;
        stopping.Dispose();
    }
}
