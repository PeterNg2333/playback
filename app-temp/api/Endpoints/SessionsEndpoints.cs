using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class SessionsEndpoints
{
    public static void MapSessions(this WebApplication app)
    {
        app.MapPost("/api/sessions", async (CreateSessionInput input, PlaybackStore store) =>
            await store.CreateSession(input.Title, input.GroupId));
        app.MapGet("/api/sessions", async (PlaybackStore store) =>
            await store.Sessions());
        app.MapPut("/api/sessions/{id}/group", async (
            string id, MoveSessionInput input, PlaybackStore store) =>
            await store.MoveSession(id, input.GroupId));
        app.MapPut("/api/sessions/{id}/translation", async (
            string id, TranslationInput input, PlaybackStore store) =>
            await store.SetTranslation(id, input.Enabled, input.Language));
        app.MapPut("/api/sessions/{id}/languages", async (
            string id, LanguagesInput input, PlaybackStore store) =>
            await store.SetLanguages(id, input.AsrLanguage, input.NoteLanguage, input.AsrModel));
        app.MapPost("/api/sessions/{id}/translation/retry", async (string id, PlaybackStore store) =>
        {
            var session = await store.Session(id)
                ?? throw new InvalidOperationException("Session not found");
            if (!session.TranslationEnabled)
                throw new InvalidOperationException("Translation is disabled");
            await store.RetryTranslations(id);
            return Results.Accepted();
        });
        app.MapGet("/api/sessions/{id}", async (string id, PlaybackStore store) =>
            await store.Session(id) is { } session
                ? Results.Ok(session)
                : Results.NotFound());
        app.MapGet("/api/sessions/{id}/sync", async (string id, string? cursor, PlaybackStore store) => await store.SyncSession(id, cursor));
        app.MapPost("/api/sessions/{id}/materials", async (
            string id, MaterialInput input, PlaybackStore store) =>
            await store.AddMaterial(id, input.Name, input.Text));
    }
}

public record CreateSessionInput(string Title, string? GroupId = null);
public record MoveSessionInput(string? GroupId);
public record TranslationInput(bool Enabled, string Language);
public record LanguagesInput(string AsrLanguage, string NoteLanguage, string? AsrModel = null);
public record MaterialInput(string Name, string Text);
