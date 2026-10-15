namespace Playback.Application.Assistance;
public interface IWorkLog
{
    void LogInformation(string message, params object?[] values);
    void LogWarning(Exception error, string message, params object?[] values);
    void LogError(Exception error, string message, params object?[] values);
}
