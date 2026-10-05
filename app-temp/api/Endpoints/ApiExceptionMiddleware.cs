using Playback.Api.Activity;
namespace Playback.Api.Endpoints;

public sealed class ApiExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Playback.Api.Security.WorkspaceNotFoundException)
        {
            await WriteError(context, 404, "Resource not found");
        }
        catch (InvalidOperationException ex)
        {
            await WriteError(context, 409, AiActivity.SafeError(ex));
        }
        catch (HttpRequestException ex)
        {
            await WriteError(context, 502, AiActivity.SafeError(ex));
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
