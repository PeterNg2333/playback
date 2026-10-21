using Playback.Application.Recording;
using Playback.Application.Workspace;
namespace Playback.Api.Endpoints;

public static class SessionsEndpoints
{
    public static void MapSessions(this WebApplication app)
    {
        app.MapPost("/api/sessions", async (CreateSessionInput input, WorkspaceCommands store) =>
            await store.CreateSession(input.Title, input.GroupId));
        app.MapDelete("/api/sessions/{id}", async (string id, RecordingCommands capture) =>
        {
            await capture.DeleteSession(id);
            return Results.NoContent();
        });
        app.MapGet("/api/sessions", async (WorkspaceCommands store) =>
            await store.Sessions());
        app.MapPut("/api/sessions/{id}/group", async (
            string id, MoveSessionInput input, WorkspaceCommands store) =>
            await store.MoveSession(id, input.GroupId));
        app.MapPut("/api/sessions/{id}/translation", async (
            string id, TranslationInput input, WorkspaceCommands store) =>
            await store.SetTranslation(id, input.Enabled, input.Language));
        app.MapPut("/api/sessions/{id}/languages", async (
            string id, LanguagesInput input, WorkspaceCommands store) =>
            await store.SetLanguages(id, input.AsrLanguage, input.NoteLanguage, input.AsrModel));
        app.MapPost("/api/sessions/{id}/translation/retry", async (string id, WorkspaceCommands store) =>
        {
            await store.RetryTranslations(id);
            return Results.Accepted();
        });
        app.MapGet("/api/sessions/{id}", async (string id, WorkspaceCommands store) =>
            await store.Session(id) is { } session
                ? Results.Ok(session)
                : Results.NotFound());
        app.MapGet("/api/sessions/{id}/lineage", async (string id, string? node, int? depth, WorkspaceCommands store) =>
            await store.Lineage(id, node, depth ?? 2));
        app.MapGet("/api/sessions/{id}/sync", async (string id, string? cursor, WorkspaceCommands store) => await store.SyncSession(id, cursor));
        app.MapPost("/api/sessions/{id}/materials", async (
            string id, MaterialInput input, WorkspaceCommands store) =>
            await store.AddMaterial(id, input.Name, input.Text));
    }
}

public record CreateSessionInput(string Title, string? GroupId = null);
public record MoveSessionInput(string? GroupId);
public record TranslationInput(bool Enabled, string Language);
public record LanguagesInput(string AsrLanguage, string NoteLanguage, string? AsrModel = null);
public record MaterialInput(string Name, string Text);
