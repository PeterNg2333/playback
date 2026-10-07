using Playback.Application.Workspace;
namespace Playback.Api.Endpoints;
public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app) => app.MapGet("/api/health", (RuntimeQueries queries) => queries.Health(new(
        typeof(HealthEndpoints).Assembly.ManifestModule.ModuleVersionId.ToString(), System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), typeof(HealthEndpoints).Assembly.Location)));
}
