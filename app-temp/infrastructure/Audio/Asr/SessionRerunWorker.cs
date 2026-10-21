using Microsoft.Extensions.DependencyInjection;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Application.Activity;
using Playback.Application.Recording;

namespace Playback.Api.Audio.Asr;

// Runs user-requested session reruns. Jobs and windows are persisted, so a restart resumes at the next pending window.
public sealed class SessionRerunWorker(SessionRerun rerun, AsrQueue queue, ILogger<SessionRerunWorker> logger) : BackgroundService
{
    readonly SemaphoreSlim wake = new(0, 1);
    public void Wake() { if (wake.CurrentCount == 0) try { wake.Release(); } catch (SemaphoreFullException) { } }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (PlaybackEnvironment.Offline || PlaybackEnvironment.ValidationApi) return;
        while (!ct.IsCancellationRequested)
        {
            await wake.WaitAsync(TimeSpan.FromSeconds(15), ct);
            if (queue.LiveBacklog) continue; // Live transcription keeps priority over a rerun.
            try { await rerun.RunPending(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning("Session rerun paused: {Error}", AiActivity.SafeError(ex)); }
        }
    }

    public static SessionRerun Create(IServiceProvider s)
    {
        var store = s.GetRequiredService<PlaybackStore>();
        var notes = s.GetRequiredService<NoteAgent>();
        var capture = s.GetRequiredService<IRecordingControl>();
        return new SessionRerun(store.Reruns(), s.GetRequiredService<IAsrAdapter>(), new TranscriptionAudio(), store.RerunAudio(),
            s.GetRequiredService<AiActivity>(), id => capture.Status() is { State: not "idle" } status && status.SessionId == id,
            notes.UpdateEvidence, async (id, ct) => { await notes.WritePending(id, ct); }, async (id, ct) => { await notes.Rebuild(id, ct); });
    }
}
