using Playback.Application.Recording;
namespace Playback.Api.Endpoints;

public static class CaptureEndpoints
{
    public static void MapCapture(this WebApplication app)
    {
        app.MapGet("/api/capture/status", (RecordingCommands capture) => capture.Status());
        app.MapPost("/api/capture/start", (CaptureInput input, RecordingCommands capture) => capture.Start(input.SessionId, input.SourceMode));
        app.MapPost("/api/capture/stop", (RecordingCommands capture) => capture.Stop());
        app.MapPost("/api/capture/pause", (RecordingCommands capture) => capture.Pause());
        app.MapPost("/api/capture/resume", (RecordingCommands capture) => capture.Resume());
        // Recording in the browser: chunks go to /api/chunks; this opens, renews and closes their stream.
        app.MapPut("/api/sessions/{sessionId}/streams/{streamId}", async (
            string sessionId, string streamId, StreamInput input, UploadedRecordingCommands streams) =>
        {
            await streams.Update(sessionId, streamId, input.Closed, input.Stop);
            return Results.Ok(new { streamId, input.Closed });
        });
    }
}

public record CaptureInput(string SessionId, string SourceMode = "microphone");
public record StreamInput(bool Closed, bool Stop = false);
