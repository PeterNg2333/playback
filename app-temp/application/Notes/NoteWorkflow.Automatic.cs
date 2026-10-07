using System.Text.Json;
using System.Text.Json.Nodes;
using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

public sealed partial class NoteWorkflow
{
    public void Tick(IEnumerable<string> ids, CancellationToken ct) => scheduler.Tick(ids, ct);
    public Task Drain() => Task.WhenAll(scheduler.Active);
    public bool AutomaticOrganization { get; set; }

    async Task Automatic(string id, CancellationToken ct)
    {
        var sessionLock = SessionLock(id);
        if (!await sessionLock.WaitAsync(0, ct)) return;
        NoteGateRecord? state = null;
        NoteActivity? call = null;
        string? preparationHash = null;
        var inputPrepared = false;
        int? baseVersion = null;
        try
        {
            var effective = processingPolicy is null ? new(ProcessingPolicy.Defaults, "0:0") : await processingPolicy.Effective(id);
            var policy = effective.Values;
            var now = clock.GetUtcNow().UtcDateTime;
            state = await repository.Gate(id) ?? new NoteGateRecord { SessionId = id };
            var policyChanged = state.PolicyRevision != effective.Revision;
            var newStop = state.FlushRequested && state.EvaluatedFlushVersion != state.FlushVersion;
            var boundary = boundaries is null ? null : await boundaries.Latest(id);
            var newBoundary = boundary is not null && boundary.Id != state.EvaluatedBoundaryId;
            // Polling and provider admission are separate. Deadline/Stop can bypass the decision cadence.
            var local = NoteAdmissionPolicy.LocalAdmission(state, policy, now);
            if (!newStop && !newBoundary && local is not ("stop" or "deadline") && !policyChanged &&
                now < (state.LastCheckedAt ?? startedAt).AddSeconds(policy.CheckIntervalSeconds)) return;
            if (!newBoundary && state.LastCheckedAt is null && !state.FlushRequested && now < startedAt.AddSeconds(policy.CheckIntervalSeconds)) return;
            if (!policyChanged && !newStop && !newBoundary && state.Status == "failed" && RetryBlocked(state) &&
                now < (state.LastCheckedAt ?? startedAt).AddSeconds(policy.CheckIntervalSeconds)) return;
            state.LastCheckedAt = now;
            state.PolicyRevision = effective.Revision;
            state.PolicySnapshot = policy;
            state.EvaluatedFlushVersion = state.FlushVersion;
            if (policyChanged) state.Attempts = 0;

            var session = await evidence.Session(id);
            if (session is null) return;
            var boundaryReady = newBoundary && (now >= boundary!.DetectedAt.AddSeconds(policy.BoundarySettleSeconds) ||
                !session.PendingAudio.Any(x => boundary!.SourceThroughMs.TryGetValue(x.SourceId, out var through) && x.EndMs <= through));
            var written = session.Transcripts.Where(x => x.NoteStatus != "completed" && session.CurrentNote?.Coverage.Any(c =>
                    c.SourceId == x.Id && c.Status == "covered" && c.ContentHash == ContentHash.Of(x.SourceText) &&
                    c.PointIds.Any(p => session.CurrentNote.Sections.Any(s => s.Points.Any(point => point.Id == p)))) == true)
                .Select(x => x.Id).ToList();
            if (written.Count > 0)
            {
                await evidence.MarkNotes(written, "completed");
                session = (await evidence.Session(id))!;
            }
            var pending = NoteInput.Pending(session);
            var materials = NoteInput.Materials(session.Materials, session.CurrentNote);
            if (pending.Count == 0 && materials.Count == 0)
            {
                state.LastCheckedAt = now;
                state.PolicyRevision = effective.Revision;
                state.PolicySnapshot = policy;
                state.FirstPendingAt = null;
                if (boundaryReady && !session.AwaitingTranscripts) state.EvaluatedBoundaryId = boundary!.Id;
                if (state.FlushRequested && session.AwaitingTranscripts) state.Status = "awaiting-asr";
                else if (state.Status is not ("completed" or "deferred")) state.Status = "settled";
                await repository.SaveGate(state);
                if (state.FlushRequested && !session.AwaitingTranscripts)
                    await repository.AcknowledgeFlush(id, state.FlushVersion);
                if (AutomaticOrganization)
                {
                    var candidate = session.Sections.FirstOrDefault(NoteAdmissionPolicy.ShouldOrganize);
                    if (candidate is not null) await Revise(session, [], [candidate], ct, organize: true);
                }
                return;
            }

            baseVersion = session.NoteVersion;
            preparationHash = "input-error:" + ContentHash.Of(session.NoteVersion + ":" + session.NoteLanguage + EvidenceIdentity(pending, materials));
            if (state.InputHash == preparationHash && !policyChanged && !newStop && RetryBlocked(state))
            { await repository.SaveGate(state); return; }
            var context = NoteInput.ContextSections(session.Sections, pending);
            var input = NoteInput.Bounded(session, pending, context, materials, "automatic");
            inputPrepared = true;
            var evidenceHash = EvidenceIdentity(pending, materials);
            // A settled filler/no-change batch is free until input, policy or explicit Stop changes.
            if (!policyChanged && !newStop && !boundaryReady && state.SettledEvidenceHash == evidenceHash &&
                state.SettledNoteVersion == session.NoteVersion && state.SettledLanguage == session.NoteLanguage)
            {
                state.LastCheckedAt = now;
                await repository.SaveGate(state);
                await SettleFlush(session, state);
                return;
            }
            var identity = JsonNode.Parse(input.Text)!.AsObject();
            identity.Remove("trigger");
            var hash = ContentHash.Of(NoteGatePrompt.PromptVersion + effective.Revision + identity.ToJsonString());
            var same = state.InputHash == hash && !policyChanged && !newStop && !boundaryReady;
            if (!same)
            {
                state.Attempts = 0;
                state.RetryAt = null;
                state.InputHash = hash;
                state.GenerationRequested = false;
                state.GateFailure = "";
                if (policyChanged) state.SettledEvidenceHash = "";
            }
            if (state.FirstPendingAt is null || state.Status is "completed" or "deferred")
                state.FirstPendingAt = pending.Where(x => x.ConfirmedAt.HasValue).Select(x => x.ConfirmedAt!.Value)
                    .DefaultIfEmpty(now).Min();
            state.PolicyRevision = effective.Revision;
            state.PolicySnapshot = policy;
            state.EvidenceHash = evidenceHash;
            state.LastCheckedAt = now;
            state.EvaluatedFlushVersion = state.FlushVersion;
            local = NoteAdmissionPolicy.LocalAdmission(state, policy, now, boundaryReady);
            // Writer backoff is respected. A failed gate cannot prevent a local deadline.
            if (same && state.Status == "failed" && RetryBlocked(state) && (state.GenerationRequested || local is null))
            { await repository.SaveGate(state); return; }
            if (local is not null && !state.GenerationRequested)
            {
                state.AdmissionReason = state.GateFailure.Length > 0 ? local + "-after-gate-failure" : local;
                if (boundaryReady) state.EvaluatedBoundaryId = boundary!.Id;
                state.GenerationRequested = true;
                state.Attempts = 0;
                state.RetryAt = null;
                state.Status = "allowed";
                await repository.SaveGate(state);
                call = await activity.Begin(id, "Note admission", "local", "processing policy", pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)), session.NoteVersion);
                activity.Context(call, "processing-policy-v1", JsonSerializer.Serialize(policy), state.AdmissionReason);
                await activity.End(call, "completed", $"{state.AdmissionReason}: queue notes; local admission, saved Jev decision remains {state.Decision}");
                call = null;
            }
            if (!state.GenerationRequested)
            {
                if (same && state.Status == "wait") { await repository.SaveGate(state); return; }
                await repository.SaveGate(state); // Persist the deadline before making a provider request.
                call = await activity.Begin(id, "Jev note gate", "typesafe", "discovery",
                    pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)), session.NoteVersion);
                activity.Context(call, NoteGatePrompt.PromptVersion, JsonSerializer.Serialize(new { policy, questions = NoteGatePrompt.Questions }), input.Text);
                call.ScheduleDelayMs = pending.LastOrDefault()?.ConfirmedAt is { } confirmedAt
                    ? Math.Max(0, (long)(call.StartedAt - confirmedAt).TotalMilliseconds) : null;
                await activity.Start(call);
                if (!jev.IsConfigured) throw new InvalidOperationException("Jev note gate is not configured; pending speech is retained");
                var decision = await jev.Decide(input.Text, ct);
                var allow = NoteAdmissionPolicy.AllowsDecision(decision.Action ?? (decision.Allow ? "update" : "wait"), decision.Probability, decision.Confidence, policy);
                state.Decision = allow ? "allow" : "wait";
                state.Probability = decision.Probability;
                state.Confidence = decision.Confidence;
                state.Model = decision.Model;
                state.UsageJson = decision.UsageJson;
                state.PromptVersion = NoteGatePrompt.PromptVersion;
                state.GateFailure = "";
                state.Attempts = 0;
                state.RetryAt = null;
                state.ChangedAt = now;
                state.WaitCount = allow ? 0 : state.WaitCount + 1;
                state.Status = allow ? "allowed" : "wait";
                state.AdmissionReason = allow ? "jev-allow" : "continuation";
                state.GenerationRequested = allow;
                call.UsageJson = decision.UsageJson;
                call.ProviderLatencyMs = decision.LatencyMs;
                await repository.SaveGate(state);
                await activity.End(call, "completed", allow ? "allow: queue section notes now" : "wait: input retained until continuation, Stop or local deadline", decision.Model);
                call = null;
                if (!allow) return;
            }

            if (!gemini.IsConfigured) throw new InvalidOperationException("Gemini is not configured; admission and pending sources are retained");
            state.Status = "writing";
            await repository.SaveGate(state);
            await Revise(session, pending, context, ct, admissionReason: state.AdmissionReason);
            var latest = await evidence.Session(id);
            var unchanged = latest is not null && EvidenceIdentity(NoteInput.Pending(latest), NoteInput.Materials(latest.Materials, latest.CurrentNote)) == evidenceHash;
            state.Status = unchanged ? "deferred" : "completed";
            state.SettledEvidenceHash = evidenceHash;
            state.SettledNoteVersion = latest?.NoteVersion ?? session.NoteVersion;
            state.SettledLanguage = latest?.NoteLanguage ?? session.NoteLanguage;
            state.Attempts = 0;
            state.RetryAt = null;
            state.FirstPendingAt = null;
            state.ChangedAt = clock.GetUtcNow().UtcDateTime;
            await repository.SaveGate(state);
            if (latest is not null) await SettleFlush(latest, state);
        }
        catch (Exception ex)
        {
            if (call is null && state is not null && !inputPrepared)
            {
                call = await activity.Begin(id, "Note input preparation", "local", "section input limits", basedOnVersion: baseVersion);
                activity.Context(call, NoteInstructions.Version, "Prepare bounded complete sources; no provider request was made.", id);
                if (state.InputHash != preparationHash) state.Attempts = 0;
                state.InputHash = preparationHash ?? state.InputHash;
                state.GenerationRequested = false;
            }
            if (call is not null) await activity.Fail(call, ex);
            if (state is not null)
            {
                if (!state.GenerationRequested && inputPrepared) state.GateFailure = "Jev failed; see Activity";
                state.Status = "failed";
                state.Attempts++;
                state.RetryAt = NoteAdmissionPolicy.RetryAt(state.Attempts, clock.GetUtcNow().UtcDateTime);
                await repository.SaveGate(state);
            }
            activity.Warning("Note job failed", id, ex);
        }
        finally { sessionLock.Release(); }
    }

    async Task SettleFlush(NoteSession session, NoteGateRecord state)
    {
        if (state.FlushRequested && !session.AwaitingTranscripts &&
            !session.Transcripts.Any(x => x.NoteStatus is "pending" or "processing" or "failed") &&
            (NoteInput.Materials(session.Materials, session.CurrentNote).Count == 0 ||
             EvidenceIdentity(NoteInput.Pending(session), NoteInput.Materials(session.Materials, session.CurrentNote)) == state.SettledEvidenceHash))
            await repository.AcknowledgeFlush(session.Id, state.FlushVersion);
    }
    static string EvidenceIdentity(IEnumerable<NoteTranscript> pending, IEnumerable<NoteMaterial> materials) => ContentHash.Of(JsonSerializer.Serialize(new
    {
        sources = pending.Select(x => new { x.Id, hash = ContentHash.Of(x.SourceText) }),
        materials = materials.Select(x => new { x.Id, hash = ContentHash.Of(x.Text) })
    }));
    bool RetryBlocked(NoteGateRecord state) => NoteAdmissionPolicy.RetryBlocked(state, clock.GetUtcNow().UtcDateTime);
}
