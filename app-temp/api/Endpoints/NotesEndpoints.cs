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
        app.MapGet("/api/sessions/{id}/notes/coverage", async (string id, NoteCommands notes) =>
            await notes.Coverage(id) is { } report ? Results.Ok(report) : Results.NotFound());
        app.MapPost("/api/sessions/{id}/notes/repair", async (string id, CoverageRepairInput input, NoteWorkflow notes, CancellationToken ct) =>
            await notes.RepairCoverage(id, input.BasedOnVersion, ct));
        app.MapGet("/api/sessions/{id}/notes/{version:int}", async (string id, int version, NoteCommands notes) =>
            await notes.Version(id, version) is { } note ? Results.Ok(note) : Results.NotFound());
        app.MapGet("/api/sessions/{id}/notes/{version:int}/recovery", async (string id, int version, NoteCommands notes) => await notes.Recovery(id, version));
        app.MapPost("/api/sessions/{id}/notes/{version:int}/recovery", async (string id, int version, RecoveryInput input, NoteCommands notes) =>
            await notes.ApplyRecovery(id, version, input.BasedOnVersion, input.SelectedIds));
        app.MapPost("/api/sessions/{id}/notes/organize", async (string id, OrganizeInput input, NoteWorkflow notes, CancellationToken ct) =>
            await notes.Organize(id, input.SectionId, input.BasedOnVersion, ct));
        app.MapPost("/api/sessions/{id}/notes/{version:int}/restore", async (string id, int version, int? basedOnVersion, NoteCommands notes) =>
            await notes.RestoreNote(id, version, basedOnVersion));
        app.MapGet("/api/sessions/{id}/notes/edits", async (string id, NoteCommands notes) =>
            (await notes.History(id)).Select(note => new
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
            string id, NoteWorkflow notes, CancellationToken ct) =>
        {
            return await notes.Generate(id, ct);
        });
    }
}
public record NoteInput(string Markdown, int? BasedOnVersion = null);
public record RecoveryInput(int BasedOnVersion, List<string> SelectedIds);
public record OrganizeInput(string SectionId, int BasedOnVersion);
public record CoverageRepairInput(int BasedOnVersion);
