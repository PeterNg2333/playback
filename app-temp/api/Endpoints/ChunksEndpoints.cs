using Playback.Api.Services.Audio;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class ChunksEndpoints
{
    public static void MapChunks(this WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/chunks/retry", async (
            string sessionId, RetryChunksInput input, AsrQueue asr) =>
        {
            await asr.Retry(sessionId, input.ChunkIds);
            return Results.Accepted(value: new { status = "queued" });
        });
        app.MapPost("/api/chunks", async (
            HttpRequest request, PlaybackStore store, AsrQueue asr, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.ContentLength is null or > 26_000_000)
                return Results.BadRequest(new { error = "A bounded multipart upload is required" });
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length is < 44 or > 25_000_000)
                return Results.BadRequest(new { error = "Expected a WAV file in field file" });
            var chunk = await store.SaveChunk(form, file, ct);
            if (chunk.Status is "transcribed" or "asr-empty" or "silent" or "asr-error" or "asr-manual")
                return Results.Ok(new { chunk.Id, chunk.Status });
            asr.Enqueue(chunk.Id);
            return Results.Accepted(
                $"/api/sessions/{chunk.SessionId}",
                new { chunk.Id, status = "pending-asr" });
        });
        app.MapGet("/api/chunks/{id}/audio", (string id, PlaybackStore store) =>
            store.Audio(id) is { } path
                ? Results.File(path, "audio/wav", enableRangeProcessing: true)
                : Results.NotFound());
        app.MapGet("/api/sessions/{sessionId}/audio/segments/{index:int}", async (
            string sessionId, int index, string? source, SessionAudioRenderer renderer) =>
            await renderer.Render(sessionId, index, source) is { } wav
                ? Results.File(wav, "audio/wav")
                : Results.NotFound());
    }
}

public sealed record RetryChunksInput(string[] ChunkIds);
