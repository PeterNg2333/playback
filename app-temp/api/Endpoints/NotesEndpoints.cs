using Playback.Api.Notes;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class NotesEndpoints
{
    public static void MapNotes(this WebApplication app)
    {
        app.MapPost("/api/sessions/{id}/notes", async (string id, NoteInput input, NoteCommands notes) =>
            await notes.SaveNote(id, input.Markdown, "user", basedOnVersion: input.BasedOnVersion));
        app.MapGet("/api/sessions/{id}/notes", async (string id, NoteCommands notes) => await notes.History(id));
        app.MapGet("/api/sessions/{id}/notes/history", async (string id, int? before, int? limit, NoteCommands notes) =>
            await notes.Page(id, before, limit ?? 20));
        app.MapGet("/api/sessions/{id}/notes/{version:int}", async (string id, int version, NoteCommands notes) =>
            await notes.Version(id, version) is { } note ? Results.Ok(note) : Results.NotFound());
        app.MapPost("/api/sessions/{id}/notes/block", async (string id, BlockEditInput input, NoteCommands notes) =>
            await notes.EditBlock(id, input.BasedOnVersion, input.BlockId, input.Body));
        app.MapPost("/api/sessions/{id}/notes/block/lock", (string id, BlockLockInput input, NoteCommands notes) =>
            notes.LockBlock(id, input.BasedOnVersion, input.BlockId, input.Locked));
        app.MapPost("/api/sessions/{id}/notes/instruct", (string id, InstructionInput input, NoteWorkflow notes, CancellationToken ct) =>
            notes.Instruct(id, input.Instruction, input.TopicId, input.BlockId, input.BasedOnVersion, ct));
        app.MapPost("/api/sessions/{id}/notes/{version:int}/restore", async (string id, int version, int? basedOnVersion, NoteCommands notes) =>
            await notes.RestoreNote(id, version, basedOnVersion));
        app.MapGet("/api/sessions/{id}/notes/edits", async (string id, NoteCommands notes) =>
            (await notes.History(id)).Select(note => new
            {
                note.Version,
                note.BasedOnVersion,
                note.Author,
                note.CreatedAt,
                note.Edits
            }));
        // Revise with AI: write every window that is due, then outline and polish.
        app.MapPost("/api/sessions/{id}/notes/generate", (string id, NoteWorkflow notes, CancellationToken ct) => notes.Generate(id, ct));
    }
}
public record NoteInput(string Markdown, int? BasedOnVersion = null);
public record BlockEditInput(int BasedOnVersion, string BlockId, string Body);
public record BlockLockInput(int BasedOnVersion, string BlockId, bool Locked);
// TopicId or BlockId narrows the instruction; a BlockId makes it a comment on that block.
public record InstructionInput(int BasedOnVersion, string Instruction, string? TopicId = null, string? BlockId = null);
