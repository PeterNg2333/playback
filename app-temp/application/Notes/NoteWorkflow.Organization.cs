using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

public sealed partial class NoteWorkflow
{
    // A review of crowded third-level subtopics sees at most this many of them, with their blocks.
    const int MaxReviewedSubtopics = 16;
    const int MaxRememberedLevels = 32;

    async Task OrganizeIfDue(NoteSession session, EffectiveProcessingPolicy effective, CancellationToken ct)
    {
        if (organization is null || session.CurrentNote is null || !effective.Values.OrganizationEnabled || !writer.IsConfigured) return;
        if (!await organizationSlots.WaitAsync(0, ct)) return;
        var state = new NoteOrganizationState {Id = session.Id};
        try
        {
            state = await organization.Organization(session.Id) ?? state;
            var note = session.CurrentNote;
            // A committed output wins over an interrupted work-state acknowledgement.
            if (state.Status is "organizing" or "failed" && note.InputHash == state.InputHash && note.Author == "agent organization")
            {
                state.OrganizedMeaningfulRevision = note.MeaningfulRevision; state.LastOrganizedAt = note.CreatedAt;
                state.Status = "completed"; state.Attempts = 0; state.RetryAt = null;
                await organization.SaveOrganization(state, state.Revision);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            var due = NoteOrganization.Due(note, state, effective.Values, now);
            if (due.Reason is null) return;
            // A crowded level that can take a parent is regrouped from the whole outline (titles only). A large topic,
            // or a parent with over six third-level subtopics, is reviewed together with its subtopics and their
            // blocks, so blocks can move between them (a split, or a merge that empties a subtopic).
            var outlineOnly = effective.Values.OrganizationScope == "document-outline" || due.Reason == "crowded-level";
            var candidates = NoteOrganization.EligibleTopics(session.Sections).ToList();
            var affected = candidates.Where(x => due.AffectedTopics.Contains(x.Id)).ToList();
            var (parents, limit) = due.Reason == "crowded-subtopics" ? (1, MaxReviewedSubtopics + 1) : (3, 8);
            var scope = outlineOnly ? candidates : affected.Take(parents)
                .SelectMany(topic => candidates.Where(x => x.Id == topic.Id || NoteStructure.IsWithin(x, topic.Id, session.Sections))
                    .OrderBy(x => x.Id != topic.Id)).Distinct().Take(limit).ToList();
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
            // Leaving a crowded level as it is is a valid review; it is not asked again until a topic joins or leaves it.
            state.ReviewedLevels = state.ReviewedLevels.Concat(NoteOrganization.StillCrowdedAfterReview(latest.Sections, due.Reason, due.AffectedTopics))
                .Distinct().TakeLast(MaxRememberedLevels).ToList();
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
