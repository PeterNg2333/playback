using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;
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
    }
}
