using Playback.Api.Services.Ai.Agents;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class NotesEndpoints
{
    public static void MapNotes(this WebApplication app)
    {
        app.MapPost("/api/sessions/{id}/notes", async (string id, NoteInput input, PlaybackStore store) =>
            await store.SaveNote(id, input.Markdown, "user"));
        app.MapGet("/api/sessions/{id}/notes", async (string id, PlaybackStore store) =>
            await store.NoteHistory(id));
        app.MapPost("/api/sessions/{id}/notes/generate", async (
            string id, PlaybackStore store, NoteAgent notes, CancellationToken ct) =>
        {
            return await notes.Generate(id, ct, allowRevision: true);
        });
    }
}
