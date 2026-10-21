using Playback.Api.Db;
using Playback.Application.Activity;
using Playback.Application.Recording;
using Playback.Infrastructure.Activity;
namespace Playback.Api.Audio.Asr;
// Compatibility adapter for capture/recovery callers. There is one transcription use case.
public sealed class AsrProcessor(PlaybackStore store, IAsrAdapter asr, ILogger<AsrProcessor> logger, AiActivity activity)
{
    readonly Transcription workflow = new(store, asr, new WorkLog<AsrProcessor>(logger), new TranscriptionAudio(), activity);
    public const string PromptVersion = Transcription.PromptVersion;
    public const string Instructions = Transcription.Instructions;
    public Task Process(string id, CancellationToken ct) => workflow.Process(id, ct);
    public static bool NetworkPermissionDenied(Exception error) => Transcription.NetworkPermissionDenied(error);
}
