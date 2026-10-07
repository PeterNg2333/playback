using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

public sealed partial class NoteWorkflow
{
    async Task OrganizeIfDue(NoteSession session, EffectiveProcessingPolicy effective, CancellationToken ct)
    {
        if (organization is null || session.CurrentNote is null || !effective.Values.OrganizationEnabled || !gemini.IsConfigured) return;
        if (!await organizationSlots.WaitAsync(0, ct)) return;
        var state = new NoteOrganizationState {Id = session.Id};
        try
        {
            state = await organization.Organization(session.Id) ?? state;
            var note = session.CurrentNote;
            // A committed output wins over an interrupted work-state acknowledgement.
            if (state.Status == "organizing" && note.InputHash == state.InputHash && note.Author == "agent organization")
            {
                state.OrganizedMeaningfulRevision = note.MeaningfulRevision; state.LastOrganizedAt = note.CreatedAt;
                state.Status = "completed"; state.Attempts = 0; state.RetryAt = null;
                await organization.SaveOrganization(state, state.Revision);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            var due = NoteOrganization.Due(note, state, effective.Values, now);
            if (due.Reason is null) return;
            var outlineOnly = effective.Values.OrganizationScope == "document-outline";
            var candidates = NoteOrganization.EligibleTopics(session.Sections).ToList();
            var affected = candidates.Where(x => due.AffectedTopics.Contains(x.Id)).ToList();
            var scope = outlineOnly ? candidates : affected.Take(3).ToList();
            var hash = ContentHash.Of("organization:" + effective.Revision + ":" + session.NoteVersion + ":" + string.Join("|", scope.Select(x => x.Id)));
            if (state.InputHash == hash && (state.Status == "review-required" || state.Attempts >= 3 || state.RetryAt > now)) return;
            if (state.InputHash != hash) state.Attempts = 0;
            state.InputHash = hash; state.PolicyRevision = effective.Revision; state.PolicySnapshot = effective.Values;
            state.DueReason = due.Reason; state.BasedOnVersion = session.NoteVersion;
            state.PendingTopics = outlineOnly ? [] : affected.Skip(3).Select(x => x.Id).ToList();
            state.PendingDirtyRevisions = due.DirtyRevisions; state.PendingDirtySince = due.DirtySince;
            if (scope.Count == 0)
            {
                state.OrganizedMeaningfulRevision = note.MeaningfulRevision; state.Status = "protected-scope";
                await organization.SaveOrganization(state, state.Revision); return;
            }
            state.Status = "organizing"; state.Attempts++; state.RetryAt = now.AddSeconds(30 * state.Attempts);
            await organization.SaveOrganization(state, state.Revision);
            var outcome = await Revise(session, [], scope, ct, organize: true, outlineOnly: outlineOnly, automaticOrganization: true, persistenceHash: hash);
            if (outcome is NoteRewritePending) { state.Status = "review-required"; await organization.SaveOrganization(state, state.Revision); return; }
            var latest = await evidence.Session(session.Id) ?? throw new InvalidOperationException("Session disappeared during organization");
            state.OrganizedMeaningfulRevision = latest.CurrentNote?.MeaningfulRevision ?? note.MeaningfulRevision;
            state.LastOrganizedAt = clock.GetUtcNow().UtcDateTime; state.Status = "completed"; state.Attempts = 0; state.RetryAt = null;
            await organization.SaveOrganization(state, state.Revision);
        }
        catch (Exception ex)
        {
            state.Status = "failed";
            await organization.SaveOrganization(state, state.Revision);
            activity.Warning("Automatic organization retained the current document", session.Id, ex);
        }
        finally { organizationSlots.Release(); }
    }
}
