using Playback.Application.Assistance;
namespace Playback.Infrastructure.Activity;
public sealed class WorkLog<T>(ILogger<T> logger) : IWorkLog
{
    public void LogInformation(string message, params object?[] values) => logger.LogInformation(message, values);
    public void LogWarning(Exception error, string message, params object?[] values) => logger.LogWarning(error, message, values);
    public void LogError(Exception error, string message, params object?[] values) => logger.LogError(error, message, values);
}
