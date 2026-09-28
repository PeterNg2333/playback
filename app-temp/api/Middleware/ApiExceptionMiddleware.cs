namespace Playback.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (InvalidOperationException ex)
        {
            await WriteError(context, 409, Playback.Api.Services.Ai.AiActivity.SafeError(ex));
        }
        catch (HttpRequestException ex)
        {
            await WriteError(context, 502, Playback.Api.Services.Ai.AiActivity.SafeError(ex));
        }
        catch (System.Text.Json.JsonException)
        {
            await WriteError(context, 502, "Provider returned invalid JSON");
        }
        catch (MongoDB.Driver.MongoException)
        {
            await WriteError(context, 503, "Local MongoDB is unavailable");
        }
        catch (TimeoutException)
        {
            await WriteError(context, 503, "Local MongoDB is unavailable");
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteError(context, 504, "Provider request timed out; retry explicitly. Saved audio and notes are retained.");
        }
    }

    private static Task WriteError(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
