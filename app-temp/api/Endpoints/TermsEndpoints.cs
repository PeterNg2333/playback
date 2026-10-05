using Playback.Api.Terms;
using Playback.Api.Providers;
namespace Playback.Api.Endpoints;

public static class TermsEndpoints
{
    public static void MapTerms(this WebApplication app)
    {
        app.MapPost("/api/explain", async (ExplainInput input, GeminiLanguageModel gemini, CancellationToken ct) =>
            Results.Ok(await gemini.GroundedExplain(input.Term, ct)));
        app.MapPost("/api/terms/rank", async (TermInput input, JevTermClassifier jev, CancellationToken ct) =>
            Results.Ok(await jev.Rank(input.Term, ct, input.Context)));
        app.MapPost("/api/terms/evaluate-synthetic", async (SyntheticTermComparison comparison, CancellationToken ct) =>
            Results.Ok(await comparison.Evaluate(ct)));
        app.MapPost("/api/sessions/{id}/terms/review", async (string id, TermReviewAgent reviewer, CancellationToken ct) =>
            Results.Ok(await reviewer.Review(id, 3, ct)));
        app.MapPost("/api/sessions/{id}/terms/{insightId}/explain", async (
            string id, string insightId, bool? detail, TermReviewAgent reviewer, CancellationToken ct) =>
            Results.Ok(await reviewer.Explain(id, insightId, ct, detail == true)));
    }
}

public record ExplainInput(string Term);
public record TermInput(string Term, string Context = "");
