using System.Collections.Concurrent;
using Playback.Domain.Models;
using Playback.Api.Activity;
using Playback.Domain.Assistance;
namespace Playback.Api.Terms;
public sealed class TermReview(ITermRepository store, ITermClassifier jev, ITextGenerator gemini, IWorkLog logger, AiActivity activity)
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> reviewGates = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> explainGates = new();
    public static List<TermCandidate> Pending(SessionView session, int limit) => session.Terms
        .Where(candidate => !session.TermInsights.Any(insight =>
            insight.Id == TermInsight.IdFor(session.Id, candidate.Text, candidate.Context, session.NoteLanguage)))
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
            activity.Context(call, "term-rank-v1", System.Text.Json.JsonSerializer.Serialize(TermRanking.Questions), TermRanking.State(candidate.Text, candidate.Context));
            await activity.Start(call);
            TermInsight insight;
            try
            {
                var rank = await jev.Rank(candidate.Text, ct, candidate.Context);
                call.ProviderLatencyMs = rank.Cached ? null : rank.LatencyMs;
                call.UsageJson = !rank.Cached && rank.Usage.ValueKind == System.Text.Json.JsonValueKind.Object && rank.Usage.EnumerateObject().Any() ? rank.Usage.GetRawText() : null;
                insight = await store.SaveTermRanking(id, candidate, rank, session.NoteLanguage);
                await activity.End(call, rank.Cached ? "cache-hit" : "completed",
                    $"Explain={rank.JevProbability:F3}; {rank.JevRank}; confidence={rank.JevConfidence:F3}; highlight={insight.Highlight}", rank.Model);
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
            if (insight.Highlight) await Explain(id, insight.Id, ct);
        }
        // A failed explanation can be retried without ranking or searching completed terms again.
        foreach (var insight in session.TermInsights.Where(x => x.Highlight && x.Explanation is null && x.OutputLanguage == session.NoteLanguage).Take(limit))
            await Explain(id, insight.Id, ct);
        logger.LogInformation("Term review completed: {SessionId}, {Count} candidates", id, candidates.Count);
        return (await store.Session(id))!.TermInsights;
        } finally { gate.Release(); }
    }

    public async Task<TermInsight> Explain(string sessionId, string insightId, CancellationToken ct, bool detail = false)
    {
        var gate = explainGates.GetOrAdd(insightId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var insight = await store.FindTermInsight(sessionId, insightId)
                ?? throw new InvalidOperationException("Term insight not found");
            if (!insight.Highlight) throw new InvalidOperationException("This term was not selected for explanation");
            var call = await activity.Begin(sessionId, "Term explanation: " + insight.Term, "vertex / Google Search",
                gemini.Model, insight.TranscriptIds.Concat(insight.MaterialIds));
            var input = $"Term: {insight.Term}\nContext: {insight.Context}\nOutput language: {NoteLanguage.OutputDescription(insight.OutputLanguage)}";
            activity.Context(call, ModelInstructions.ExplanationPromptVersion, ModelInstructions.Explanation, input);
            await activity.Start(call);
            try
            {
                var cached = insight.Explanation is not null && (!detail || insight.ExplanationVersion == ModelInstructions.ExplanationPromptVersion);
                if (!cached)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    GroundedResult explanation;
                    try { explanation = await gemini.GroundedExplain(input, ct, usage => call.UsageJson = usage); }
                    finally { call.ProviderLatencyMs = watch.ElapsedMilliseconds; }
                    insight = await store.SaveTermExplanation(sessionId, insightId, explanation, detail);
                }
                await activity.End(call, cached ? "cache-hit" : "completed", $"{insight.Evidence.Count} sources; saved hover explanation");
                return (await store.FindTermInsight(sessionId, insightId))!;
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
        }
        finally { gate.Release(); }
    }

}
