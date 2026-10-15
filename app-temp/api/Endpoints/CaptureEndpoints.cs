using Playback.Api.Audio.Recording;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class CaptureEndpoints
{
    public static void MapCapture(this WebApplication app)
    {
        app.MapGet("/api/capture/status", (Playback.Application.Recording.RecordingCommands capture) => capture.Status());
        app.MapPost("/api/capture/start", (CaptureInput input, Playback.Application.Recording.RecordingCommands capture) => capture.Start(input.SessionId, input.SourceMode));
        app.MapPost("/api/capture/stop", (Playback.Application.Recording.RecordingCommands capture) => capture.Stop());
        app.MapPost("/api/capture/pause", (Playback.Application.Recording.RecordingCommands capture) => capture.Pause());
        app.MapPost("/api/capture/resume", (Playback.Application.Recording.RecordingCommands capture) => capture.Resume());
    }
}

public record CaptureInput(string SessionId, string SourceMode = "microphone");
