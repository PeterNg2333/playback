using Playback.Application.Workspace;
using Playback.Application.Ask;
using Playback.Application.Notes;
using Playback.Application.Recording;
using Playback.Infrastructure.Activity;
using Playback.Infrastructure.Audio.Recording;
using Playback.Infrastructure.Providers;
using Playback.Infrastructure.Storage;
using Playback.Infrastructure.Terms;
namespace Playback.Infrastructure.Workspace;
public sealed class RuntimeProjections(PlaybackStore store, GeminiLanguageModel gemini, JevTermClassifier jev, IAsrAdapter asr, AiFlow flow) : IRuntimeProjections
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
            sessionSync = true,
            threeSpeedNotes = true,
            notePromptVersion = NotePrompts.Version,
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
            uploadStreams = true,
            sessionLanguageSettings = true,
            liveAsrPreview = true,
            audioChunkMilliseconds = WindowsAudioCaptureService.ChunkMilliseconds,
            asrModels = asr.Model.Provider == "openrouter" ? AsrModelOptions.OpenRouter : [],
            asrStreaming = asr is IStreamingAsrAdapter,
            asr = asr.Model
        };
}
