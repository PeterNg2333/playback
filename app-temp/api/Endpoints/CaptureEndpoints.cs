using Playback.Api.Services.Audio;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class CaptureEndpoints
{
    public static void MapCapture(this WebApplication app)
    {
        app.MapGet("/api/capture/status", (WindowsAudioCaptureService capture) => capture.Status());
        app.MapPost("/api/capture/start", async (CaptureInput input, WindowsAudioCaptureService capture) =>
            await capture.Start(input.SessionId, input.SourceMode));
        app.MapPost("/api/capture/stop", async (WindowsAudioCaptureService capture, PlaybackStore store) => {
            var id = capture.Status().SessionId;
            var status = await capture.Stop();
            if (id is not null) await store.RequestNoteFlush(id);
            return status;
        });
        app.MapPost("/api/capture/pause", async (WindowsAudioCaptureService capture) => await capture.Pause());
        app.MapPost("/api/capture/resume", async (WindowsAudioCaptureService capture) => await capture.Resume());
    }
}
