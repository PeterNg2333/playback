using Playback.Api.Services.Audio;
namespace Playback.Api.Endpoints;

public static class CaptureEndpoints
{
    public static void MapCapture(this WebApplication app)
    {
        app.MapGet("/api/capture/status", (WindowsAudioCaptureService capture) => capture.Status());
        app.MapPost("/api/capture/start", async (CaptureInput input, WindowsAudioCaptureService capture) =>
        {
            if (!input.ConsentConfirmed)
                throw new InvalidOperationException("Confirm recording consent before starting capture");
            return await capture.Start(input.SessionId);
        });
        app.MapPost("/api/capture/stop", async (WindowsAudioCaptureService capture) => await capture.Stop());
        app.MapPost("/api/capture/pause", async (WindowsAudioCaptureService capture) => await capture.Pause());
        app.MapPost("/api/capture/resume", async (WindowsAudioCaptureService capture) => await capture.Resume());
    }
}
