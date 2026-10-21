using Playback.Application.Activity;
using Playback.Application.Recording;
using Playback.Infrastructure.Storage;

namespace Playback.Infrastructure.Audio.Asr;

public sealed class TranscriptReviewWorker(PlaybackStore store, IAsrAdapter asr, AiActivity activity,
    NoteAgent notes, AsrQueue queue, ILogger<TranscriptReviewWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (PlaybackEnvironment.Offline || PlaybackEnvironment.ValidationApi || PlaybackEnvironment.ExternalAsrPaused ||
            Environment.GetEnvironmentVariable("PLAYBACK_ASR_REVIEW") == "no") return;
        var workflow = new TranscriptReview(store.TranscriptReviews(), asr, new TranscriptionAudio(), activity,
            async (id, token) => { if (PlaybackEnvironment.AutomaticNotes) await notes.Generate(id, token); }, notes.UpdateEvidence);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (queue.LiveBacklog) continue;
            try { await workflow.Run(DateTime.UtcNow, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning("ASR review retained first-pass transcripts: {Error}", AiActivity.SafeError(ex)); }
        }
    }

}
