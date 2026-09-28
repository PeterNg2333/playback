using Playback.Api.Services.Ai.Providers;
using Playback.Api.Db;
using Playback.Api.Services.Audio;
namespace Playback.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app)
    {
        app.MapGet("/api/health", async (PlaybackStore store, GeminiLanguageModel gemini, JevTermClassifier jev, IAsrAdapter asr) => new
        {
            mongo = await store.IsReady(),
            build = typeof(HealthEndpoints).Assembly.ManifestModule.ModuleVersionId.ToString(),
            startedAt = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            executable = typeof(HealthEndpoints).Assembly.Location,
            elapsedRecordingClock = true,
            aiActivity = true,
            groundedChatFallback = true,
            savedAsrRecovery = Environment.GetEnvironmentVariable("PLAYBACK_RESUME_SAVED_ASR") != "no",
            database = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_DATABASE") ?? "playback_prototype",
            gemini = gemini.IsConfigured,
            jev = jev.IsConfigured,
            automaticAsr = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") != "yes"
                && Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") != "yes",
            asrPaused = Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") == "yes",
            autoNotes = Environment.GetEnvironmentVariable("PLAYBACK_AUTO_NOTES") != "no",
            autoTerms = Environment.GetEnvironmentVariable("PLAYBACK_AUTO_TERMS") != "no",
            manualAsrRetry = true,
            sessionAudioMix = true,
            recordingSourceSelection = true,
            sessionLanguageSettings = true,
            liveAsrPreview = true,
            audioChunkMilliseconds = LiveAsrSession.ChunkMilliseconds,
            asrModels = asr.Model.Provider == "openrouter" ? AsrModelOptions.OpenRouter : [],
            asrStreaming = asr is IStreamingAsrAdapter,
            asr = asr.Model
        });
    }
}
