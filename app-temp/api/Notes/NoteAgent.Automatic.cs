using System.Text.Json;
using System.Text.Json.Nodes;
using Playback.Api.Activity;
using Playback.Api.Db;

namespace Playback.Api.Notes;

// Automatic notes. Every 10 s the scheduler admits sessions with changed confirmed input, the Jev note
// gate answers "update now" or "wait", and an allow runs one note revision. The decision and its input
// identity are saved per session, so unchanged input is never evaluated twice, even after a restart.
public sealed partial class NoteAgent
{
    // Three failures stop retries until the input changes; each retry waits 30 s longer than the last.
    const int MaxFailedAttempts = 3;
    static readonly TimeSpan RetryStep = TimeSpan.FromSeconds(30);
    // After three waits, a large or growing backlog is written anyway, labelled as a safeguard, not a Jev allow.
    const int WaitsBeforeSafeguard = 3;
    const int SafeguardPendingChars = 2_000;
    static readonly TimeSpan OrganizationScanInterval = TimeSpan.FromMinutes(30);

    DateTimeOffset nextOrganization;

    public void Tick(IEnumerable<string> ids, CancellationToken ct) => scheduler.Tick(ids, ct);
    public Task Drain() => Task.WhenAll(scheduler.Active);

    // Long or repetitive sections are organized automatically only with PLAYBACK_AUTO_ORGANIZE=yes.
    public static bool ShouldOrganize(NoteSection section) => !section.UserEdited && section.OrganizedVersion != section.Version &&
        (section.Markdown.Length > 9000 && section.Points.Count > 12 || section.Points.Count > 8 &&
            section.Points.Select(x => x.Text).Distinct().Count() < section.Points.Count * .7);

    async Task ScanPending()
    {
        if (PlaybackEnvironment.Offline) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                try
                {
                    if (!PlaybackEnvironment.AutomaticNotes || !scheduler.Due) continue;
                    var ids = await store.SessionsWithPendingNotes();
                    if (PlaybackEnvironment.AutomaticOrganization && nextOrganization <= clock.GetUtcNow())
                    {
                        nextOrganization = clock.GetUtcNow().Add(OrganizationScanInterval);
                        ids = ids.Concat(await store.SessionsNeedingOrganization()).Distinct().ToList();
                    }
                    ids = ids.Where(PlaybackEnvironment.AllowsAutomaticWork).ToList();
                    scheduler.Tick(ids, stopping.Token);
                }
                catch (Exception ex) when (!stopping.IsCancellationRequested)
                {
                    logger.LogWarning("Note scan failed: {Error}", AiActivity.SafeError(ex));
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    async Task Automatic(string id, CancellationToken ct)
    {
        var sessionLock = SessionLock(id);
        if (!await sessionLock.WaitAsync(0, ct)) return;
        NoteGateRecord? state = null;
        ActivityRecord? call = null;
        var inputPrepared = false;
        string? preparationHash = null;
        int? baseVersion = null;
        try
        {
            var session = await store.Session(id);
            if (session is null) return;
            // Recover a save -> status-update crash from the actual written point ledger, never the old source-ID metadata.
            var written = session.Transcripts.Where(x => x.NoteStatus != "completed" && session.CurrentNote?.Coverage.Any(c =>
                    c.SourceId == x.Id && c.Status == "covered" && c.ContentHash == ContentHash.Of(x.SourceText) &&
                    c.PointIds.Any(p => session.CurrentNote.Sections.Any(s => s.Points.Any(point => point.Id == p)))) == true)
                .Select(x => x.Id)
                .ToList();
            if (written.Count > 0)
            {
                await store.MarkNotes(written, "completed");
                session = (await store.Session(id))!;
            }
            var pending = NoteInput.Pending(session);
            var materials = NoteInput.Materials(session.Materials, session.CurrentNote);
            if (pending.Count == 0 && materials.Count == 0)
            {
                var flush = await store.NoteGate(id);
                if (flush?.FlushRequested == true && !session.Chunks.Any(x => ChunkStatus.AwaitingTranscript(x.Status)))
                    await store.AcknowledgeNoteFlush(id, flush.FlushVersion);
                if (PlaybackEnvironment.AutomaticOrganization)
                {
                    var candidate = NoteSections.Current(session).FirstOrDefault(ShouldOrganize);
                    if (candidate is not null) await Revise(session, [], [candidate], ct, organize: true);
                }
                return;
            }

            state = await store.NoteGate(id) ?? new NoteGateRecord { SessionId = id };
            baseVersion = session.NoteVersion;
            preparationHash = "input-error:" + ContentHash.Of(JsonSerializer.Serialize(new
            {
                session.NoteVersion,
                session.NoteLanguage,
                sources = pending.Select(x => new { x.Id, hash = ContentHash.Of(x.SourceText) }),
                materials = materials.Select(x => new { x.Id, hash = ContentHash.Of(x.Text) })
            }));
            if (state.InputHash == preparationHash && state.Status == "failed" && RetryBlocked(state)) return;
            var context = NoteInput.ContextSections(NoteSections.Current(session), pending);
            var input = NoteInput.Bounded(session, pending, context, materials, state.FlushRequested ? "stop:" + state.FlushVersion : "automatic");
            inputPrepared = true;

            // The input identity leaves out the trigger text; a new Stop is tracked by its flush version instead.
            var identity = JsonNode.Parse(input.Text)!.AsObject();
            identity.Remove("trigger");
            var hash = ContentHash.Of(JevNoteGate.PromptVersion + identity.ToJsonString());
            var same = state.InputHash == hash && (!state.FlushRequested || state.EvaluatedFlushVersion == state.FlushVersion);
            if (same && state.Status is "wait" or "completed") return;
            if (same && state.Status == "failed" && RetryBlocked(state)) return;
            if (!same)
            {
                state.Attempts = 0;
                state.InputHash = hash;
                state.GenerationRequested = false;
            }
            state.EvaluatedFlushVersion = state.FlushVersion;
            if (!(same && state.GenerationRequested))
            {
                call = await activity.Begin(id, "Jev note gate", "typesafe", "discovery",
                    pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)), session.NoteVersion);
                activity.Context(call, JevNoteGate.PromptVersion, JsonSerializer.Serialize(JevNoteGate.Questions), input.Text);
                // ASR arrival timestamps were not stored by earlier builds. Do not invent a schedule latency.
                call.ScheduleDelayMs = pending.LastOrDefault()?.ConfirmedAt is { } confirmedAt
                    ? Math.Max(0, (long)(call.StartedAt - confirmedAt).TotalMilliseconds) : null;
                await activity.Start(call);
                if (!jev.IsConfigured) throw new InvalidOperationException("Jev note gate is not configured; pending speech is retained");
                var decision = await jev.Decide(input.Text, ct);
                state.Decision = decision.Allow ? "allow" : "wait";
                state.Probability = decision.Probability;
                state.Confidence = decision.Confidence;
                state.Model = decision.Model;
                state.UsageJson = decision.UsageJson;
                state.PromptVersion = JevNoteGate.PromptVersion;
                state.ChangedAt = clock.GetUtcNow().UtcDateTime;
                state.WaitCount = decision.Allow ? 0 : state.WaitCount + 1;
                var unwritten = session.Transcripts.Count(x => x.NoteStatus != "completed" && x.NoteStatus != "suppressed" && x.SourceText.Length > 0);
                var safeguard = !decision.Allow && (state.FlushRequested || state.WaitCount >= WaitsBeforeSafeguard &&
                    (pending.Sum(x => x.SourceText.Length) >= SafeguardPendingChars || unwritten > pending.Count));
                state.Status = decision.Allow || safeguard ? "allowed" : "wait";
                state.GenerationRequested = decision.Allow || safeguard;
                call.UsageJson = decision.UsageJson;
                call.ProviderLatencyMs = decision.LatencyMs;
                await store.SaveNoteGate(state);
                await activity.End(call, "completed", decision.Allow ? "allow: queue section notes now" :
                    safeguard ? "wait: explicit stop/backlog safeguard queues uncertain content; this is not a Jev allow" :
                    "wait: pending input retained; unchanged input will not be evaluated again", decision.Model);
                call = null;
                if (state.Status == "wait") return;
            }

            if (!gemini.IsConfigured) throw new InvalidOperationException("Gemini is not configured; Jev decision and pending sources are retained");
            await Revise(session, pending, context, ct);
            state.Status = "completed";
            state.Attempts = 0;
            state.ChangedAt = clock.GetUtcNow().UtcDateTime;
            await store.SaveNoteGate(state);
            if (state.FlushRequested)
            {
                var latest = await store.Session(id);
                if (latest is not null && !latest.Chunks.Any(x => ChunkStatus.AwaitingTranscript(x.Status)) &&
                    !latest.Transcripts.Any(x => x.NoteStatus is "pending" or "processing" or "failed"))
                    await store.AcknowledgeNoteFlush(id, state.FlushVersion);
            }
        }
        catch (Exception ex)
        {
            if (call is null && state is not null && !inputPrepared)
            {
                call = await activity.Begin(id, "Note input preparation", "local", "section input limits", basedOnVersion: baseVersion);
                activity.Context(call, NoteInstructions.Version, "Prepare bounded complete source passages; no provider request was made.", state.SessionId);
                if (state.InputHash != preparationHash) state.Attempts = 0;
                state.InputHash = preparationHash!;
            }
            if (call is not null) await activity.Fail(call, ex);
            if (state is not null)
            {
                state.Status = "failed";
                state.Attempts++;
                state.RetryAt = clock.GetUtcNow().UtcDateTime.Add(RetryStep * state.Attempts);
                await store.SaveNoteGate(state);
            }
            logger.LogWarning("Note job failed for {Session}: {Error}", id, AiActivity.SafeError(ex));
        }
        finally { sessionLock.Release(); }
    }

    bool RetryBlocked(NoteGateRecord state) =>
        state.Attempts >= MaxFailedAttempts || state.RetryAt > clock.GetUtcNow().UtcDateTime;
}
