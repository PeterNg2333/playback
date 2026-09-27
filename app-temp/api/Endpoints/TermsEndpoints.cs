using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class TermsEndpoints
{
    public static void MapTerms(this WebApplication app)
    {
        app.MapPost("/api/explain", async (ExplainInput input, GeminiLanguageModel gemini, CancellationToken ct) =>
            Results.Ok(await gemini.GroundedExplain(input.Term, ct)));
        app.MapPost("/api/terms/rank", async (TermInput input, JevTermClassifier jev, CancellationToken ct) =>
            Results.Ok(await jev.Rank(input.Term, ct)));
        app.MapPost("/api/terms/evaluate-synthetic", async (SyntheticTermComparison comparison, CancellationToken ct) =>
            Results.Ok(await comparison.Evaluate(ct)));
        app.MapPost("/api/sessions/{id}/terms/review", async (string id, TermReviewAgent reviewer, CancellationToken ct) =>
            Results.Ok(await reviewer.Review(id, 3, ct)));
        app.MapPost("/api/sessions/{id}/terms/{insightId}/explain", async (
            string id, string insightId, TermReviewAgent reviewer, CancellationToken ct) =>
            Results.Ok(await reviewer.Explain(id, insightId, ct)));
    }
}
