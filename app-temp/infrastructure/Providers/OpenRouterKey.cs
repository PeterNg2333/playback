namespace Playback.Api.Providers;

// One OpenRouter credential serves both speech-to-text and text models. It is read from the process
// environment only; .env.example ships a placeholder that must never count as configured.
public static class OpenRouterKey
{
    public static string? Read() => Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") is { } key && IsUsable(key) ? key : null;

    public static bool IsUsable(string? key) => !string.IsNullOrWhiteSpace(key) && key != "your_openrouter_api_key_here";
}
