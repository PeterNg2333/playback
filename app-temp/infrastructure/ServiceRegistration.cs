using Playback.Api;
using Playback.Api.Activity;
using Playback.Api.Ask;
using Playback.Api.Audio;
using Playback.Api.Audio.Asr;
using Playback.Api.Audio.Recording;
using Playback.Api.Db;

using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Api.Terms;
using Playback.Api.Translation;

using Microsoft.Extensions.DependencyInjection;
namespace Playback.Infrastructure;
public static class ServiceRegistration
{
    public static IServiceCollection AddPlayback(this IServiceCollection services)
    {
        services.AddSingleton<PlaybackStore>();
        services.AddHostedService<StorageIndexWorker>();
        services.AddSingleton<Playback.Application.Workspace.IWorkspaceRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<Playback.Application.Workspace.WorkspaceCommands>();
        services.AddSingleton<Playback.Application.Workspace.DemoAccounts>();
        services.AddSingleton<Playback.Application.Assistance.IActivityRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<Playback.Application.Assistance.IConversationRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<Playback.Application.Assistance.ITermRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<AiActivity>();
        services.AddSingleton<AiFlow>();
        services.AddSingleton<Playback.Application.Workspace.TestingCommands>();
        services.AddSingleton<Playback.Application.Workspace.ITestingRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<Playback.Application.Workspace.RuntimeQueries>();
        services.AddSingleton<Playback.Application.Workspace.IRuntimeProjections, Playback.Infrastructure.Workspace.RuntimeProjections>();

        services.AddSingleton<GeminiLanguageModel>();
        services.AddSingleton<Playback.Application.Assistance.ITextGenerator>(s => s.GetRequiredService<GeminiLanguageModel>());
        services.AddSingleton<JevTransport>();

        services.AddSingleton<IAsrAdapter>(_ => AsrAdapters.Create());
        services.AddSingleton<AsrProcessor>();
        services.AddSingleton<AsrQueue>();
        services.AddHostedService(s => s.GetRequiredService<AsrQueue>());
        services.AddHostedService<TranscriptReviewWorker>();
        services.AddSingleton<WindowsAudioCaptureService>();
        services.AddSingleton<Playback.Application.Recording.IRecordingControl>(s => s.GetRequiredService<WindowsAudioCaptureService>());
        services.AddSingleton<Playback.Application.Recording.IRecordingAccess>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<Playback.Application.Recording.RecordingCommands>();
        services.AddSingleton<SessionAudioRenderer>();
        services.AddSingleton<Playback.Application.Recording.IChunkRepository>(s => s.GetRequiredService<PlaybackStore>());
        services.AddSingleton<Playback.Application.Recording.IAsrJobs>(s => s.GetRequiredService<AsrQueue>());
        services.AddSingleton<Playback.Application.Recording.IAudioRenderer>(s => s.GetRequiredService<SessionAudioRenderer>());
        services.AddSingleton<Playback.Application.Recording.ChunkCommands>();
        services.AddHostedService<CombinedAudioWorker>();

        services.AddSingleton<Playback.Application.Notes.INoteWriter>(s => NoteWriters.Create(s.GetRequiredService<GeminiLanguageModel>()));
        services.AddSingleton<IJevTransport>(s => s.GetRequiredService<JevTransport>());
        services.AddSingleton<NoteStoreAdapter>();
        services.AddSingleton<NoteCommands>(s => new(s.GetRequiredService<NoteStoreAdapter>(), s.GetRequiredService<NoteStoreAdapter>()));
        services.AddSingleton<NoteWorkflow>(s => new(s.GetRequiredService<NoteStoreAdapter>(), s.GetRequiredService<NoteStoreAdapter>(),
            s.GetRequiredService<Playback.Application.Notes.INoteWriter>(),
            new NoteActivityAdapter(s.GetRequiredService<AiActivity>(), s.GetRequiredService<ILogger<NoteAgent>>()), new OpenccNoteScript()));
        services.AddSingleton<NoteAgent>(s => new(s.GetRequiredService<PlaybackStore>(), s.GetRequiredService<NoteWorkflow>(),
            s.GetRequiredService<ILogger<NoteAgent>>()));
        services.AddHostedService(s => s.GetRequiredService<NoteAgent>());
        services.AddSingleton<Playback.Application.Recording.SessionRerun>(SessionRerunWorker.Create);
        services.AddSingleton<SessionRerunWorker>();
        services.AddHostedService(s => s.GetRequiredService<SessionRerunWorker>());
        services.AddSingleton<Playback.Api.Ask.IConceptGraph>(_ => new LightRagClient());
        services.AddSingleton<ConceptGraphWorker>();
        services.AddHostedService(s => s.GetRequiredService<ConceptGraphWorker>());
        services.AddSingleton<JevTermClassifier>(s => new JevTermClassifier(s.GetRequiredService<JevTransport>()));
        services.AddSingleton<Playback.Application.Assistance.ITermClassifier>(s => s.GetRequiredService<JevTermClassifier>());
        services.AddSingleton<Playback.Application.Assistance.TermAssistance>();
        services.AddSingleton<TermReview>(s => new(s.GetRequiredService<PlaybackStore>(), s.GetRequiredService<JevTermClassifier>(), s.GetRequiredService<GeminiLanguageModel>(), new Playback.Infrastructure.Activity.WorkLog<TermReview>(s.GetRequiredService<ILogger<TermReview>>()), s.GetRequiredService<AiActivity>()));
        services.AddSingleton<TermReviewAgent>();
        services.AddHostedService(s => s.GetRequiredService<TermReviewAgent>());
        services.AddSingleton<SyntheticTermComparison>();
        services.AddSingleton<ChatAgent>();

        return services;
    }
}
