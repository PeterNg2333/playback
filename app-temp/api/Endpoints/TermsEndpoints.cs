using Playback.Api.Terms;
using Playback.Api.Providers;
namespace Playback.Api.Endpoints;

public static class TermsEndpoints
{
    public static void MapTerms(this WebApplication app)
    {
        app.MapPost("/api/explain", async (ExplainInput input, Playback.Application.Assistance.TermAssistance service, CancellationToken ct) =>
            Results.Ok(await service.Explain(input.Term, ct)));
        app.MapPost("/api/terms/rank", async (TermInput input, Playback.Application.Assistance.TermAssistance service, CancellationToken ct) =>
            Results.Ok(await service.Rank(input.Term, input.Context, ct)));
        app.MapPost("/api/terms/evaluate-synthetic", async (SyntheticTermComparison comparison, CancellationToken ct) =>
            Results.Ok(await comparison.Evaluate(ct)));
        app.MapPost("/api/sessions/{id}/terms/review", async (string id, TermReview reviewer, CancellationToken ct) =>
            Results.Ok(await reviewer.Review(id, 3, ct)));
        app.MapPost("/api/sessions/{id}/terms/{insightId}/explain", async (
            string id, string insightId, bool? detail, TermReview reviewer, CancellationToken ct) =>
            Results.Ok(await reviewer.Explain(id, insightId, ct, detail == true)));
    }
}

public record ExplainInput(string Term);
public record TermInput(string Term, string Context = "");
