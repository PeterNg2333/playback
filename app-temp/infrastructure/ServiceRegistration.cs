
using Playback.Application.Activity;
using Playback.Application.Ask;
using Playback.Application.Notes;
using Playback.Application.Recording;
using Playback.Application.Terms;
using Playback.Application.Workspace;
using Playback.Infrastructure.Activity;
using Playback.Infrastructure.Audio;
using Playback.Infrastructure.Audio.Asr;
using Playback.Infrastructure.Audio.Recording;
using Playback.Infrastructure.Providers;
using Playback.Infrastructure.Storage;
using Playback.Infrastructure.Terms;
using Playback.Infrastructure.Workspace;

using Microsoft.Extensions.DependencyInjection;
namespace Playback.Infrastructure;
public static class ServiceRegistration
{
    public static IServiceCollection AddPlayback(this IServiceCollection services)
    {
        services.AddSingleton<PlaybackStore>();
        services.AddHostedService<StorageIndexWorker>();
        services.AddSingleton<IWorkspaceRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<WorkspaceCommands>();
        services.AddSingleton<DemoAccounts>();
        services.AddSingleton<IActivityRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<IConversationRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<ITermRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<AiActivity>();
        services.AddSingleton<AiFlow>();
        services.AddSingleton<TestingCommands>();
        services.AddSingleton<ITestingRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<RuntimeQueries>();
        services.AddSingleton<IRuntimeProjections, RuntimeProjections>();

        services.AddSingleton<GeminiLanguageModel>();
        services.AddSingleton<ITextGenerator>(s => s.GetRequiredService<GeminiLanguageModel>());
        services.AddSingleton<JevTransport>();

        services.AddSingleton<IAsrAdapter>(_ => AsrAdapters.Create());
        services.AddSingleton<AsrProcessor>();
        services.AddSingleton<AsrQueue>();
        services.AddHostedService(s => s.GetRequiredService<AsrQueue>());
        services.AddHostedService<TranscriptReviewWorker>();
        services.AddSingleton<WindowsAudioCaptureService>();
        services.AddSingleton<IRecordingControl>(s => s.GetRequiredService<WindowsAudioCaptureService>());
        services.AddSingleton<IRecordingAccess>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<RecordingCommands>();
        services.AddSingleton<IUploadedStreams>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<UploadedRecordingCommands>();
        services.AddSingleton<SessionAudioRenderer>();
        services.AddSingleton<IChunkRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<IAsrJobs>(s => s.GetRequiredService<AsrQueue>());
        services.AddSingleton<IAudioRenderer>(s => s.GetRequiredService<SessionAudioRenderer>());
        services.AddSingleton<ChunkCommands>();
        services.AddHostedService<CombinedAudioWorker>();

        services.AddSingleton<INoteWriter>(s => NoteWriters.Create(s.GetRequiredService<GeminiLanguageModel>()));
        services.AddSingleton<IJevTransport>(s => s.GetRequiredService<JevTransport>());
        services.AddSingleton<NoteStoreAdapter>();
        services.AddSingleton<NoteCommands>(s => new(s.GetRequiredService<NoteStoreAdapter>(), s.GetRequiredService<NoteStoreAdapter>()));
        services.AddSingleton<NoteWorkflow>(s => new(s.GetRequiredService<NoteStoreAdapter>(), s.GetRequiredService<NoteStoreAdapter>(),
            s.GetRequiredService<INoteWriter>(),
            new NoteActivityAdapter(s.GetRequiredService<AiActivity>(), s.GetRequiredService<ILogger<NoteAgent>>()), new OpenccNoteScript()));
        services.AddSingleton<NoteAgent>(s => new(s.GetRequiredService<PlaybackStore>(), s.GetRequiredService<NoteWorkflow>(),
            s.GetRequiredService<ILogger<NoteAgent>>()));
        services.AddHostedService(s => s.GetRequiredService<NoteAgent>());
        services.AddSingleton<SessionRerun>(SessionRerunWorker.Create);
        services.AddSingleton<SessionRerunWorker>();
        services.AddHostedService(s => s.GetRequiredService<SessionRerunWorker>());
        services.AddSingleton<IConceptGraph>(_ => new LightRagClient());
        services.AddSingleton<ConceptGraphWorker>();
        services.AddHostedService(s => s.GetRequiredService<ConceptGraphWorker>());
        services.AddSingleton<JevTermClassifier>(s => new JevTermClassifier(s.GetRequiredService<JevTransport>()));
        services.AddSingleton<ITermClassifier>(s => s.GetRequiredService<JevTermClassifier>());
        services.AddSingleton<TermAssistance>();
        services.AddSingleton<TermReview>(s => new(s.GetRequiredService<PlaybackStore>(), s.GetRequiredService<JevTermClassifier>(), s.GetRequiredService<GeminiLanguageModel>(), new WorkLog<TermReview>(s.GetRequiredService<ILogger<TermReview>>()), s.GetRequiredService<AiActivity>()));
        services.AddSingleton<TermReviewAgent>();
        services.AddHostedService(s => s.GetRequiredService<TermReviewAgent>());
        services.AddSingleton<SyntheticTermComparison>();
        services.AddSingleton<ChatAgent>();

        return services;
    }
}
