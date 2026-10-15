namespace Playback.Application.Workspace;
public sealed record HostBuildInfo(string Build, DateTime StartedAt, string Executable);
public interface IRuntimeProjections
{
    Task<object> Health(HostBuildInfo host);
    Task<object> Flow(string groupId, string? sessionId);
}
public sealed class RuntimeQueries(IRuntimeProjections projections)
{
    public Task<object> Health(HostBuildInfo host) => projections.Health(host);
    public Task<object> Read(string groupId, string? sessionId) => projections.Flow(groupId, sessionId);
}
