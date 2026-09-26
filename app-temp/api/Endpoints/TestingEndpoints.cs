using Playback.Api.Services.Audio;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class TestingEndpoints
{
    public static void MapTesting(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            {
                app.MapPost("/api/testing/sessions/{id}/transcripts", async (
                    string id, SyntheticTranscriptInput input, PlaybackStore store) =>
                {
                    await store.AddSyntheticTranscript(id, input.ChunkId, input.Text);
                    return Results.Ok(new { saved = true });
                });
                app.MapGet("/api/testing/sessions/{id}/translations/pending",
                    async (string id, PlaybackStore store) =>
                    (await store.PendingTranslations(sessionId: id)).Select(x => x.Id).ToArray());
            }
            app.MapDelete("/api/testing/sessions/{id}", async (
                string id, PlaybackStore store, WindowsAudioCaptureService capture) =>
            {
                if (capture.Status().SessionId == id)
                    throw new InvalidOperationException("Stop recording before test cleanup");
                await store.DeleteTestSession(id);
                return Results.NoContent();
            });
            app.MapDelete("/api/testing/groups/{id}", async (string id, PlaybackStore store) =>
            {
                await store.DeleteTestGroup(id);
                return Results.NoContent();
            });
        }
    }
}
