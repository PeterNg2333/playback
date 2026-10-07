using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class GroupsEndpoints
{
    public static void MapGroups(this WebApplication app)
    {
        app.MapGet("/api/groups", async (Playback.Application.Workspace.WorkspaceCommands store) =>
            await store.Groups());
        app.MapPost("/api/groups", async (GroupInput input, Playback.Application.Workspace.WorkspaceCommands store) =>
            await store.CreateGroup(input.Name));
        app.MapPut("/api/groups/{id}", async (string id, GroupInput input, Playback.Application.Workspace.WorkspaceCommands store) =>
            await store.RenameGroup(id, input.Name));
        app.MapDelete("/api/groups/{id}", async (string id, Playback.Application.Workspace.WorkspaceCommands store) =>
        {
            await store.DeleteGroup(id);
            return Results.NoContent();
        });
    }
}

public record GroupInput(string Name);
