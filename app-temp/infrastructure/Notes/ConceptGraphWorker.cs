using Playback.Api.Db;
using Playback.Application.Activity;
using Playback.Application.Ask;
using Playback.Application.Recording;

namespace Playback.Api.Notes;

// Indexes changed sessions into the concept graph after their transcript changes (new speech or a rerun window).
// A failure is logged and retried on the next change; it never affects transcription, notes or Ask's keyword fallback.
public sealed class ConceptGraphWorker(PlaybackStore store, IConceptGraph graph, AiActivity activity,
    IRecordingControl capture, ILogger<ConceptGraphWorker> logger) : BackgroundService
{
    readonly HashSet<string> dirty = new(StringComparer.Ordinal);
    // A sidecar that is down is retried after 30 s, doubling to 10 minutes, so the activity log is not flooded.
    readonly Dictionary<string, (int Failures, DateTime RetryAt)> backoff = new(StringComparer.Ordinal);

    // Index a session on request, such as one recorded before the graph was turned on.
    public void Request(string id) { lock (dirty) { dirty.Add(id); backoff.Remove(id); } }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!graph.Enabled || PlaybackEnvironment.ValidationApi) return;
        var index = new ConceptIndex(graph, activity);
        void Changed(string id) { lock (dirty) dirty.Add(id); }
        store.SourceChanged += Changed;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(ct))
            {
                string[] sessions;
                lock (dirty) { sessions = dirty.ToArray(); dirty.Clear(); }
                foreach (var id in sessions.Where(PlaybackEnvironment.AllowsAutomaticWork))
                {
                    bool waiting; lock (dirty) waiting = backoff.TryGetValue(id, out var wait) && wait.RetryAt > DateTime.UtcNow;
                    if (waiting) { Changed(id); continue; }
                    try
                    {
                        if (await store.Session(id) is not { } session) continue;
                        var status = capture.Status();
                        await index.Sync(session, status.State != "idle" && status.SessionId == id, ct);
                        lock (dirty) backoff.Remove(id);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        Changed(id);
                        lock (dirty)
                        {
                            var failures = backoff.GetValueOrDefault(id).Failures + 1;
                            backoff[id] = (failures, DateTime.UtcNow.AddSeconds(Math.Min(600, 30 * Math.Pow(2, failures - 1))));
                        }
                        logger.LogWarning("Concept graph index deferred: {Error}", AiActivity.SafeError(ex));
                    }
                }
            }
        }
        finally { store.SourceChanged -= Changed; }
    }
}
