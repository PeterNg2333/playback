using Playback.Api.Audio.Recording;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class CaptureEndpoints
{
    public static void MapCapture(this WebApplication app)
    {
        app.MapGet("/api/capture/status", async (WindowsAudioCaptureService capture, PlaybackStore store) =>
        {
            var status = capture.Status();
            if (status.SessionId is { } id) await store.RequireSessionOwner(id);
            return status;
        });
        app.MapPost("/api/capture/start", async (CaptureInput input, WindowsAudioCaptureService capture, PlaybackStore store) =>
        {
            await store.RequireSessionOwner(input.SessionId);
            return await capture.Start(input.SessionId, input.SourceMode);
        });
        app.MapPost("/api/capture/stop", async (WindowsAudioCaptureService capture, PlaybackStore store) => {
            var id = capture.Status().SessionId;
            if (id is not null) await store.RequireSessionOwner(id);
            var status = await capture.Stop();
            if (id is not null) await store.RequestNoteFlush(id);
            return status;
        });
        app.MapPost("/api/capture/pause", async (WindowsAudioCaptureService capture, PlaybackStore store) =>
        {
            if (capture.Status().SessionId is { } id) await store.RequireSessionOwner(id);
            return await capture.Pause();
        });
        app.MapPost("/api/capture/resume", async (WindowsAudioCaptureService capture, PlaybackStore store) =>
        {
            if (capture.Status().SessionId is { } id) await store.RequireSessionOwner(id);
            return await capture.Resume();
        });
    }
}

public record CaptureInput(string SessionId, string SourceMode = "microphone");
