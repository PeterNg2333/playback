using Playback.Api.Activity;
namespace Playback.Api.Endpoints;

public static class ActivityEndpoints
{
    public static void MapActivity(this WebApplication app)
    {
        // The activity popover polls without prompt text; a group's AI flow returns it in full.
        app.MapGet("/api/sessions/{id}/activity", async (string id, bool? includePrompt, AiActivity activity) =>
            Results.Json(await activity.Read(id), includePrompt == false ? AiActivity.NotesResponseOptions : null));
        app.MapGet("/api/groups/{id}/flow", async (string id, string? sessionId, AiFlow flow) => await flow.Read(id, sessionId));
    }
}
