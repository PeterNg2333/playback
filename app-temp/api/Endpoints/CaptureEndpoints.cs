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
    }
}

public record CaptureInput(string SessionId, string SourceMode = "microphone");
