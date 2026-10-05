using System.Security.Claims;
using Playback.Api.Db;

namespace Playback.Api.Security;

public sealed class WorkspaceNotFoundException : Exception;

// Authorize the parent session before any endpoint reads notes, chats, sources or audio.
// A missing and a foreign resource both return 404, including when the caller knows its ID.
public sealed class WorkspaceAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, PlaybackStore store)
    {
        var owner = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (owner is null) { await next(http); return; } // Login, health probe or rejected anonymous request.
        var path = http.Request.Path;
        var id = http.Request.RouteValues["id"]?.ToString() ?? http.Request.RouteValues["sessionId"]?.ToString();
        if (id is not null)
        {
            if (path.StartsWithSegments("/api/sessions") || path.StartsWithSegments("/api/testing/sessions"))
            {
                if (!await store.OwnsSession(id, owner)) throw new WorkspaceNotFoundException();
            }
            else if (path.StartsWithSegments("/api/groups") || path.StartsWithSegments("/api/testing/groups"))
            {
                if (!await store.OwnsGroup(id, owner)) throw new WorkspaceNotFoundException();
                if (http.Request.Query["sessionId"].ToString() is { Length: > 0 } selected &&
                    !await store.OwnsSession(selected, owner)) throw new WorkspaceNotFoundException();
            }
            else if (path.StartsWithSegments("/api/chunks"))
            {
                var session = id.Split('-')[0];
                if (!ChunkIdentity.IsSessionId(session) || !await store.OwnsSession(session, owner))
                    throw new WorkspaceNotFoundException();
            }
        }
        await next(http);
    }
}
