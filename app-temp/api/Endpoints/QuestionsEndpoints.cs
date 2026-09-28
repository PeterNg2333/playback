using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai;
using System.Text.Json;
using System.Threading.Channels;
namespace Playback.Api.Endpoints;

public static class QuestionsEndpoints
{
    public static void MapQuestions(this WebApplication app)
    {
        app.MapPost("/api/sessions/{id}/ask",
            async (string id, QuestionInput input, ChatAgent service, CancellationToken ct) =>
                Results.Ok(await service.Ask(id, input, ct)));
        app.MapPost("/api/sessions/{id}/ask/stream", async (string id, QuestionInput input, ChatAgent service, HttpContext http) =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(140));
            var events = Channel.CreateBounded<object>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
            http.Response.ContentType = "application/x-ndjson";
            http.Response.Headers.CacheControl = "no-store";
            var work = Task.Run(async () =>
            {
                try
                {
                    events.Writer.TryWrite(new { type = "status", text = "Checking processed session evidence…" });
                    var answer = await service.Ask(id, input, deadline.Token,
                        text => events.Writer.TryWrite(new { type = "draft", text }));
                    events.Writer.TryWrite(new { type = "result", answer });
                }
                catch (Exception ex) { events.Writer.TryWrite(new { type = "error", error = AiActivity.SafeError(ex) }); }
                finally { events.Writer.TryComplete(); }
            });
            try
            {
                await foreach (var entry in events.Reader.ReadAllAsync(http.RequestAborted))
                {
                    await http.Response.WriteAsync(JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n", http.RequestAborted);
                    await http.Response.Body.FlushAsync(http.RequestAborted);
                }
            }
            finally { deadline.Cancel(); await work; }
        });
    }
}
