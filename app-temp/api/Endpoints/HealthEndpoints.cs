using Playback.Api.Services.Ai.Providers;
using Playback.Api.Db;
namespace Playback.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/api/health", async (PlaybackStore store, GeminiLanguageModel gemini, JevTermClassifier jev) => new
        {
            mongo = await store.IsReady(),
            gemini = gemini.IsConfigured,
            jev = jev.IsConfigured,
            automaticAsr = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") != "yes"
                && Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") != "yes",
            asrPaused = Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") == "yes",
            consentFreeAsr = true
        });
    }
}
