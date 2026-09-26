using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class GroupsEndpoints
{
    public static void MapGroups(this WebApplication app)
    {
        app.MapGet("/api/groups", async (PlaybackStore store) =>
            await store.Groups());
        app.MapPost("/api/groups", async (GroupInput input, PlaybackStore store) =>
            await store.CreateGroup(input.Name));
        app.MapPut("/api/groups/{id}", async (string id, GroupInput input, PlaybackStore store) =>
            await store.RenameGroup(id, input.Name));
    }
}
