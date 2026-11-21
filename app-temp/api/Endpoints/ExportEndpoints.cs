using System.Text.Json;
using Playback.Application.Notes;
using Playback.Infrastructure.Storage;

namespace Playback.Api.Endpoints;

// Export to Word: the owner's templates, and a conversation per session in which the model writes and revises this
// meeting's document in a template's structure and tone. Every reply keeps its draft, so any version can be downloaded;
// the browser builds the .docx from the template and a draft.
public static class ExportEndpoints
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly SemaphoreSlim Writing = new(1, 1);

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

        app.MapGet("/api/sessions/{id}/export/conversation", async (string id, PlaybackStore store) =>
            (await store.ExportTurns(id)).Select(View));
        app.MapDelete("/api/sessions/{id}/export/conversation", async (string id, PlaybackStore store) =>
        {
            await store.ClearExportTurns(id);
            return Results.NoContent();
        });
        app.MapPost("/api/sessions/{id}/export/message", async (string id, ExportMessageInput input, PlaybackStore store,
            NoteExport export, CancellationToken ct) =>
        {
            var message = NoteExport.CleanMessage(input.Message);
            // No template ID: the document is laid out by the model from the owner's messages.
            var template = string.IsNullOrEmpty(input.TemplateId) ? null
                : await store.ExportTemplate(input.TemplateId) ?? throw new InvalidOperationException("Template not found");
            var turns = await store.ExportTurns(id);
            var history = turns.Select(x => new NoteExport.ExportMessage(x.Role, x.Text)).Where(x => x.Text.Length > 0).ToList();
            // A draft written for another template names that template's elements: start afresh from the minutes.
            var latest = turns.LastOrDefault(x => x.BlocksJson is not null);
            var draft = latest is not null && (latest.TemplateId ?? "") == (template?.Id ?? "")
                ? JsonSerializer.Deserialize<List<NoteExport.ExportBlock>>(latest.BlocksJson!, Json)
                : null;
            var session = await store.Session(id) ?? throw new InvalidOperationException("Session not found");
            await Writing.WaitAsync(ct);
            NoteExport.ExportRevision revision;
            try { revision = await export.Revise(id, template?.Name, template?.Outline, history, draft, message, ct); }
            finally { Writing.Release(); }
            var now = DateTime.UtcNow;
            var asked = new ExportTurnRecord { Id = Guid.NewGuid().ToString("N"), Role = "user", Text = message, TemplateId = template?.Id,
                TemplateName = template?.Name, NoteVersion = session.NoteVersion, CreatedAt = now };
            var answered = new ExportTurnRecord { Id = Guid.NewGuid().ToString("N"), Role = "assistant", Text = revision.Reply,
                BlocksJson = JsonSerializer.Serialize(revision.Blocks, Json), TemplateId = template?.Id, TemplateName = template?.Name,
                Provider = revision.Provider, Model = revision.Model, NoteVersion = session.NoteVersion, CreatedAt = now.AddMilliseconds(1) };
            await store.AddExportTurns(id, asked, answered);
            return new[] { View(asked), View(answered) };
        });
    }

    static object View(ExportTurnRecord turn) => new
    {
        turn.Id, turn.Role, turn.Text, turn.TemplateId, turn.TemplateName, turn.Provider, turn.Model, turn.NoteVersion, turn.CreatedAt,
        Blocks = turn.BlocksJson is null ? null : JsonSerializer.Deserialize<List<NoteExport.ExportBlock>>(turn.BlocksJson, Json)
    };
}

public record TemplateInput(string Name, string ContentBase64, string Outline);
public record ExportMessageInput(string? TemplateId = null, string? Message = null);
