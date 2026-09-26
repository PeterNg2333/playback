using Playback.Api.Services.Ai.Agents;
namespace Playback.Api.Endpoints;

public static class QuestionsEndpoints
{
    public static void MapQuestions(this WebApplication app)
    {
        app.MapPost("/api/sessions/{id}/ask",
            async (string id, QuestionInput input, ChatAgent service, CancellationToken ct) =>
                Results.Ok(await service.Ask(id, input, ct)));
    }
}
