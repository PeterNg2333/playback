using Playback.Api.Audio.Asr.Providers;
using Playback.Api.Providers;
namespace Playback.Api.Audio.Asr;

public static class AsrAdapters
{
    public static string ConfiguredProvider()
    {
        var configured = Environment.GetEnvironmentVariable("PLAYBACK_ASR_PROVIDER");
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        return ResolveProvider(configured, key);
    }

    public static string ResolveProvider(string? configured, string? key) => configured switch
    {
        null or "" => OpenRouterKey.IsUsable(key) ? "openrouter" : "sensevoice",
        "openrouter" or "sensevoice" => configured,
        _ => throw new InvalidOperationException("PLAYBACK_ASR_PROVIDER must be openrouter or sensevoice")
    };

    public static IAsrAdapter Create() => ConfiguredProvider() switch
    {
        "openrouter" => new OpenRouterAsrClient(),
        _ => new SenseVoiceClient()
    };
}
