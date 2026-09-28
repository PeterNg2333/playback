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
            database = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_DATABASE") ?? "playback_prototype",
            gemini = gemini.IsConfigured,
            jev = jev.IsConfigured,
            automaticAsr = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") != "yes"
                && Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") != "yes",
            asrPaused = Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") == "yes",
            autoNotes = Environment.GetEnvironmentVariable("PLAYBACK_AUTO_NOTES") != "no",
            autoTerms = Environment.GetEnvironmentVariable("PLAYBACK_AUTO_TERMS") == "yes",
            manualAsrRetry = true,
            sessionAudioMix = true,
            recordingSourceSelection = true
        });
    }
}
