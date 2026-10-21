using Playback.Application.Recording;
namespace Playback.Api.Endpoints;

public static class ChunksEndpoints
{
    public static void MapChunks(this WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/chunks/retry", async (
            string sessionId, RetryChunksInput input, ChunkCommands asr) =>
        {
            await asr.Retry(sessionId, input.ChunkIds);
            return Results.Accepted(value: new { status = "queued" });
        });
        app.MapPost("/api/chunks", async (
            HttpRequest request, ChunkCommands service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.ContentLength is null or > 26_000_000)
                return Results.BadRequest(new { error = "A bounded multipart upload is required" });
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length is < 44 or > 25_000_000)
                return Results.BadRequest(new { error = "Expected a WAV file in field file" });
            await using var content = file.OpenReadStream();
            var chunk = await service.Import(new(form.ToDictionary(x => x.Key, x => x.Value.ToString()), content, file.Length), ct);
            // Uploading a finished or failed chunk again does not queue it; failures keep their own retry path.
            if (ChunkStatus.Finished(chunk.Status) || chunk.Status is ChunkStatus.AsrError or ChunkStatus.AsrManual)
                return Results.Ok(new { chunk.Id, chunk.Status });
            return Results.Accepted(
                $"/api/sessions/{chunk.SessionId}",
                new { chunk.Id, status = ChunkStatus.PendingAsr });
        });
        app.MapGet("/api/chunks/{id}/audio", (string id, ChunkCommands store) =>
            store.ChunkAudioPath(id) is { } path
                ? Results.File(path, "audio/wav", enableRangeProcessing: true)
                : Results.NotFound());
        app.MapGet("/api/sessions/{sessionId}/audio/combined/{segmentId}", async (string sessionId, string segmentId, ChunkCommands store) =>
            await store.CombinedAudioPath(sessionId, segmentId) is { } path ? Results.File(path, "audio/wav", enableRangeProcessing: true) : Results.NotFound());
        app.MapGet("/api/sessions/{sessionId}/audio/segments/{index:int}", async (
            string sessionId, int index, string? source, ChunkCommands renderer) =>
            await renderer.Render(sessionId, index, source) is { } wav
                ? Results.File(wav, "audio/wav")
                : Results.NotFound());
    }
}

public sealed record RetryChunksInput(string[] ChunkIds);
