using Playback.Api.Db;
using Playback.Application.Notes;
using Playback.Domain.Notes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playback.Api.Endpoints;

public static class ProcessingSettingsEndpoints
{
    public static void MapProcessingSettings(this WebApplication app)
    {
        app.MapGet("/api/settings/processing", (PlaybackStore store) => store.WorkspaceProcessingSettings());
        app.MapPut("/api/settings/processing", (JsonElement input, PlaybackStore store) => store.SaveWorkspaceProcessingSettings(Read(input)));
        app.MapGet("/api/sessions/{id}/processing-settings", (string id, PlaybackStore store) => store.SessionProcessingSettings(id));
        app.MapPut("/api/sessions/{id}/processing-settings", (string id, JsonElement input, PlaybackStore store) => store.SaveSessionProcessingSettings(id, Read(input)));
    }
    static ProcessingSettingsUpdate Read(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("expectedRevision", out _))
            throw new ProcessingPolicyValidationException("Expected settings revision is required");
        try
        {
            return input.Deserialize<ProcessingSettingsUpdate>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new ProcessingPolicyValidationException("Settings update is required");
        }
        catch (JsonException) { throw new ProcessingPolicyValidationException("Unknown or invalid processing setting"); }
    }
}
