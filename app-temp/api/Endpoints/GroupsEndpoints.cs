using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class GroupsEndpoints
{
    public static void MapGroups(this WebApplication app)
    {
        app.MapGet("/api/groups", async (PlaybackStore store) =>
            await store.Groups());
        app.MapGet("/api/groups/{id}/flow", async (string id, string? sessionId, Playback.Api.Services.Ai.AiFlow flow) => await flow.Read(id, sessionId));
        app.MapPost("/api/groups", async (GroupInput input, PlaybackStore store) =>
            await store.CreateGroup(input.Name));
        app.MapPut("/api/groups/{id}", async (string id, GroupInput input, PlaybackStore store) =>
            await store.RenameGroup(id, input.Name));
        app.MapDelete("/api/groups/{id}", async (string id, PlaybackStore store) =>
        {
            await store.DeleteGroup(id);
            return Results.NoContent();
        });
    }
}
