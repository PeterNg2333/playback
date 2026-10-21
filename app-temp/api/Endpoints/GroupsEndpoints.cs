using Playback.Application.Workspace;
namespace Playback.Api.Endpoints;

public static class GroupsEndpoints
{
    public static void MapGroups(this WebApplication app)
    {
        app.MapGet("/api/groups", async (WorkspaceCommands store) =>
            await store.Groups());
        app.MapPost("/api/groups", async (GroupInput input, WorkspaceCommands store) =>
            await store.CreateGroup(input.Name));
        app.MapPut("/api/groups/{id}", async (string id, GroupInput input, WorkspaceCommands store) =>
            await store.RenameGroup(id, input.Name));
        app.MapDelete("/api/groups/{id}", async (string id, WorkspaceCommands store) =>
        {
            await store.DeleteGroup(id);
            return Results.NoContent();
        });
    }
}

public record GroupInput(string Name);
