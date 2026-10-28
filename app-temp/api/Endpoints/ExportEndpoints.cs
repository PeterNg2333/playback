using Playback.Application.Notes;
using Playback.Infrastructure.Storage;

namespace Playback.Api.Endpoints;

// Export to Word: the owner's templates, and a draft of this meeting's document in a template's structure and tone.
// The browser builds the .docx from the template and the draft.
public static class ExportEndpoints
{
    public static void MapExports(this WebApplication app)
    {
        app.MapGet("/api/export-templates", async (PlaybackStore store) => await store.ExportTemplates());
        app.MapPost("/api/export-templates", async (TemplateInput input, PlaybackStore store) =>
            await store.AddExportTemplate(input.Name, input.ContentBase64, input.Outline));
        app.MapGet("/api/export-templates/{templateId}", async (string templateId, PlaybackStore store) =>
            await store.ExportTemplate(templateId) is { } template
                ? Results.Ok(new { template.Id, template.Name, ContentBase64 = Convert.ToBase64String(template.Content) })
                : Results.NotFound());
        app.MapDelete("/api/export-templates/{templateId}", async (string templateId, PlaybackStore store) =>
        {
            await store.DeleteExportTemplate(templateId);
            return Results.NoContent();
        });
        app.MapPost("/api/sessions/{id}/export/draft", async (string id, ExportDraftInput input, PlaybackStore store,
            NoteExport export, CancellationToken ct) =>
        {
            var template = await store.ExportTemplate(input.TemplateId) ?? throw new InvalidOperationException("Template not found");
            return await export.Draft(id, template.Name, template.Outline, input.Instruction, ct);
        });
    }
}

public record TemplateInput(string Name, string ContentBase64, string Outline);
public record ExportDraftInput(string TemplateId, string? Instruction = null);
