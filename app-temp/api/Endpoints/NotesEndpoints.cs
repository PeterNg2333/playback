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
        app.MapPost("/api/sessions/{id}/notes/structure", async (string id, StructureInput input, NoteCommands notes) =>
            await notes.ApplyStructure(id, input.BasedOnVersion, input.Operations));
        app.MapPost("/api/sessions/{id}/notes/block", async (string id, BlockEditInput input, NoteCommands notes) =>
            await notes.EditBlock(id, input.BasedOnVersion, input.BlockId, input.Body));
        app.MapGet("/api/sessions/{id}/notes/rewrites", (string id, NoteCommands notes) => notes.Rewrites(id));
        app.MapPost("/api/sessions/{id}/notes/rewrites/preview", (string id, OrganizeInput input, NoteWorkflow notes, CancellationToken ct) => notes.PreviewRewrite(id, input.SectionId, input.BasedOnVersion, ct));
        app.MapPost("/api/sessions/{id}/notes/rewrites/{proposalId}/accept", (string id, string proposalId, RewriteAcceptance input, NoteCommands notes) => notes.AcceptRewrite(id, proposalId, input.SemanticAccepted));
        app.MapPost("/api/sessions/{id}/notes/rewrites/{proposalId}/dismiss", (string id, string proposalId, NoteCommands notes) => notes.DismissRewrite(id, proposalId));
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
public record StructureInput(int BasedOnVersion, List<Playback.Domain.Notes.NoteTreeOperation> Operations);
public record BlockEditInput(int BasedOnVersion, string BlockId, string Body);
public record RewriteAcceptance(bool SemanticAccepted);
public record BlockLockInput(int BasedOnVersion, string BlockId, bool Locked);
// TopicId or BlockId narrows the instruction; a BlockId makes it a comment on that block.
public record InstructionInput(int BasedOnVersion, string Instruction, string? TopicId = null, string? BlockId = null);
