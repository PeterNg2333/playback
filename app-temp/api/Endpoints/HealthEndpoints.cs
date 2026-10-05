using Playback.Api.Audio.Recording;
using Playback.Api.Providers;
using Playback.Api.Notes;
using Playback.Api.Terms;
using Playback.Api.Db;
using Playback.Api.Audio.Asr;
using Playback.Api.Ask;
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
            chatPromptVersion = ChatAgent.PromptVersion,
            chatConversations = true,
            sectionNotes = true,
            noteCoverage = true,
            sessionSync = true,
            jevNoteGate = jev.IsConfigured,
            noteDecisionIntervalSeconds = 10,
            notePromptVersion = NoteInstructions.Version,
            savedAsrRecovery = PlaybackEnvironment.ResumeSavedAsr,
            database = PlaybackEnvironment.Database,
            gemini = gemini.IsConfigured,
            geminiModel = gemini.Model,
            jev = jev.IsConfigured,
            automaticAsr = PlaybackEnvironment.AutomaticAsr,
            asrPaused = PlaybackEnvironment.ExternalAsrPaused,
            autoNotes = PlaybackEnvironment.AutomaticNotes,
            autoTerms = PlaybackEnvironment.AutomaticTerms,
            manualAsrRetry = true,
            sessionAudioMix = true,
            recordingSourceSelection = true,
            localCapture = OperatingSystem.IsWindows(),
            sessionLanguageSettings = true,
            liveAsrPreview = true,
            audioChunkMilliseconds = WindowsAudioCaptureService.ChunkMilliseconds,
            asrModels = asr.Model.Provider == "openrouter" ? AsrModelOptions.OpenRouter : [],
            asrStreaming = asr is IStreamingAsrAdapter,
            asr = asr.Model
        });
    }
}
