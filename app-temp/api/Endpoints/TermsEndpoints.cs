using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;
namespace Playback.Api.Endpoints;

public static class TermsEndpoints
{
    public static void MapTerms(this WebApplication app)
    {
        app.MapPost("/api/explain", async (ExplainInput input, GeminiLanguageModel gemini, CancellationToken ct) =>
        {
            if (!input.ConsentConfirmed)
                throw new InvalidOperationException("Confirm public web search for this term");
            return Results.Ok(await gemini.GroundedExplain(input.Term, ct));
        });
        app.MapPost("/api/terms/rank", async (TermInput input, JevTermClassifier jev, CancellationToken ct) =>
        {
            if (!input.ConsentConfirmed)
                throw new InvalidOperationException("Confirm external term ranking before sending the term");
            return Results.Ok(await jev.Rank(input.Term, ct));
        });
        app.MapPost("/api/terms/evaluate-synthetic", async (ConsentInput input, SyntheticTermComparison comparison, CancellationToken ct) =>
        {
            if (!input.Confirmed)
                throw new InvalidOperationException("Confirm external synthetic evaluation");
            return Results.Ok(await comparison.Evaluate(ct));
        });
    }
}
