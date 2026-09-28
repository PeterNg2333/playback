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
        app.MapPost("/api/sessions/{id}/notes/{version:int}/restore", async (string id, int version, PlaybackStore store) =>
        {
            var note = (await store.NoteHistory(id)).SingleOrDefault(x => x.Version == version)
                ?? throw new InvalidOperationException("Saved note version not found in this session");
            return await store.SaveNote(id, note.Markdown, "user restore");
        });
        app.MapGet("/api/sessions/{id}/notes/edits", async (string id, PlaybackStore store) =>
            (await store.NoteHistory(id)).Select(note => new
            {
                note.Version,
                note.BasedOnVersion,
                note.Author,
                note.CreatedAt,
                note.InputTranscriptIds,
                note.InputMaterialIds,
                note.Edits
            }));
        app.MapPost("/api/sessions/{id}/notes/generate", async (
            string id, PlaybackStore store, NoteAgent notes, CancellationToken ct) =>
        {
            return await notes.Generate(id, ct, allowRevision: true);
        });
    }
}
