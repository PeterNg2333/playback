using Playback.Application.Recording;
using Playback.Domain.Models;
using Playback.Infrastructure.Audio.Asr;
namespace Playback.Api.Endpoints;

// Rerun a session: ASR over longer windows and/or the notes (incremental or rebuilt). One job per session at a time.
public static class RerunEndpoints
{
    public static void MapRerun(this WebApplication app)
    {
        app.MapPost("/api/sessions/{id}/rerun", async (string id, RerunRequest input, SessionRerun rerun, SessionRerunWorker worker) =>
        {
            var job = await rerun.Start(id, input);
            worker.Wake();
            return Results.Accepted(value: RerunView.Of(job));
        });
        app.MapGet("/api/sessions/{id}/rerun", async (string id, SessionRerun rerun) =>
            await rerun.Status(id) is { } job ? Results.Ok(RerunView.Of(job)) : Results.NoContent());
        app.MapPost("/api/sessions/{id}/rerun/cancel", async (string id, SessionRerun rerun) =>
            await rerun.Cancel(id) is { } job ? Results.Ok(RerunView.Of(job)) : Results.NoContent());
    }
}
