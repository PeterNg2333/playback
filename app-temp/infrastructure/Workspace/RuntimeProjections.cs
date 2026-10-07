using Playback.Api.Audio.Recording;
using Playback.Api.Providers;
using Playback.Api.Notes;
using Playback.Api.Terms;
using Playback.Api.Db;
using Playback.Api.Audio.Asr;
using Playback.Api.Ask;
using Playback.Api.Activity; using Playback.Application.Workspace;
namespace Playback.Infrastructure.Workspace;
public sealed class RuntimeProjections(PlaybackStore store, GeminiLanguageModel gemini, JevTermClassifier jev, IAsrAdapter asr, ProcessingPolicy defaults, AiFlow flow) : IRuntimeProjections
{
    public Task<object> Flow(string groupId, string? sessionId) => flow.Read(groupId, sessionId);
    public async Task<object> Health(HostBuildInfo host) => new
        {
            mongo = await store.IsReady(),
            build = host.Build,
            startedAt = host.StartedAt,
            executable = host.Executable,
            elapsedRecordingClock = true,
            aiActivity = true,
            groundedChatFallback = true,
            chatPromptVersion = ChatAgent.PromptVersion,
            chatConversations = true,
            sectionNotes = true,
            noteCoverage = true,
            sessionSync = true,
            jevNoteGate = jev.IsConfigured,
            noteDecisionIntervalSeconds = defaults.CheckIntervalSeconds,
            noteProcessingSettings = true,
            noteMaxPendingWaitSeconds = defaults.MaxPendingWaitSeconds,
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
        };
}
