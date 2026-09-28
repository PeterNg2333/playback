using System.Collections.Concurrent;
using Playback.Api.Db;
using Playback.Api.Services.Ai.Providers;
using Playback.Api.Terms;
using Playback.Api.Services.Ai;

namespace Playback.Api.Services.Ai.Agents;

public sealed class TermReviewAgent : IAsyncDisposable
{
    readonly PlaybackStore store;
    readonly JevTermClassifier jev;
    readonly GeminiLanguageModel gemini;
    readonly ILogger<TermReviewAgent> logger;
    readonly AiActivity activity;
    readonly ConcurrentDictionary<string, DateTime> pendingSessions = new();
    readonly ConcurrentDictionary<string, int> attempts = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> reviewGates = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> explainGates = new();
    readonly CancellationTokenSource stopping = new();
    readonly Task scanner;

    public TermReviewAgent(PlaybackStore store, JevTermClassifier jev, GeminiLanguageModel gemini, ILogger<TermReviewAgent> logger, AiActivity activity)
    {
        this.store = store;
        this.jev = jev;
        this.gemini = gemini;
        this.logger = logger;
        this.activity = activity;
        store.SourceChanged += Queue;
        scanner = Task.Run(ScanPending);
    }

    public static List<TermCandidate> Pending(SessionView session, int limit) => session.Terms
        .Where(candidate => !session.TermInsights.Any(insight =>
            insight.Id == PlaybackStore.TermInsightId(session.Id, candidate.Text, candidate.Context, session.NoteLanguage)))
        .Take(limit).ToList();

    public async Task<List<TermInsight>> Review(string id, int limit, CancellationToken ct)
    {
        var gate = reviewGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try {
        var session = await store.Session(id) ?? throw new InvalidOperationException("Session not found");
        var candidates = Pending(session, limit);
        foreach (var candidate in candidates)
        {
            var call = await activity.Begin(id, "Jev ranking: " + candidate.Text, "typesafe", "discovery",
                candidate.TranscriptIds.Concat(candidate.MaterialIds));
            await activity.Start(call);
            TermInsight insight;
            try
            {
                var rank = (JevRankResult)await jev.Rank(candidate.Text, ct, candidate.Context);
                insight = await store.SaveTermRanking(id, candidate, rank, session.NoteLanguage);
                await activity.End(call, rank.Cached ? "cache-hit" : "completed",
                    $"Explain={rank.JevProbability:F3}; {rank.JevRank}; confidence={rank.JevConfidence:F3}; highlight={insight.Highlight}", rank.Model);
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
            if (insight.Highlight) await Explain(id, insight.Id, ct);
        }
        // A failed explanation can be retried without ranking or searching completed terms again.
        foreach (var insight in session.TermInsights.Where(x => x.Highlight && x.AddedToNoteVersion is null).Take(limit))
            await Explain(id, insight.Id, ct);
        logger.LogInformation("Term review completed: {SessionId}, {Count} candidates", id, candidates.Count);
        return (await store.Session(id))!.TermInsights;
        } finally { gate.Release(); }
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
            var call = await activity.Begin(sessionId, "Term explanation: " + insight.Term, "vertex / Google Search",
                "gemini-3.5-flash-lite", insight.TranscriptIds.Concat(insight.MaterialIds));
            await activity.Start(call);
            try
            {
                var cached = insight.Explanation is not null;
                if (!cached)
                {
                    var explanation = await gemini.GroundedExplain($"Term: {insight.Term}\nContext: {insight.Context}\n" +
                        $"Output language: {LanguageSettings.OutputDescription(insight.OutputLanguage)}", ct);
                    insight = await store.SaveTermExplanation(sessionId, insightId, explanation);
                }
                await store.AddSupplementToNote(sessionId, insightId);
                await activity.End(call, cached ? "cache-hit" : "completed", $"{insight.Evidence.Count} sources; explanation added to notes");
                return (await store.TermInsight(sessionId, insightId))!;
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
        }
        finally { gate.Release(); }
    }

    void Queue(string id)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_VALIDATION_PORT") == "5081" &&
            Environment.GetEnvironmentVariable("PLAYBACK_VALIDATION_SESSION") != id) return;
        if (attempts.GetValueOrDefault(id) >= 3) return;
        if (pendingSessions.Count < 128) pendingSessions.TryAdd(id, DateTime.UtcNow.AddSeconds(4));
    }
    async Task ScanPending()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes") return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                if (Environment.GetEnvironmentVariable("PLAYBACK_AUTO_TERMS") != "no" && jev.IsConfigured)
                {
                    try
                    {
                        foreach (var item in pendingSessions.Where(x => x.Value <= DateTime.UtcNow).Take(1))
                        {
                            pendingSessions.TryRemove(item.Key, out _);
                            try {
                                await Review(item.Key, 3, stopping.Token);
                                attempts.TryRemove(item.Key, out _);
                                var view = await store.Session(item.Key);
                                if (view is not null && Pending(view, 1).Count > 0) Queue(item.Key);
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
        store.SourceChanged -= Queue;
        stopping.Cancel();
        await scanner;
        stopping.Dispose();
    }
}
