using System.Text.Json;

namespace Playback.Domain.Notes;

public sealed class NoteContentChange
{
    public int Revision { get; set; }
    public DateTime At { get; set; }
    public List<string> TopicIds { get; set; } = [];
}
public sealed class NoteOrganizationState
{
    public string Id { get; set; } = "";
    public int Revision { get; set; }
    public int OrganizedMeaningfulRevision { get; set; }
    public DateTime? LastOrganizedAt { get; set; }
    public string Status { get; set; } = "idle";
    public string DueReason { get; set; } = "";
    public string InputHash { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime? RetryAt { get; set; }
    public string PolicyRevision { get; set; } = "";
    public ProcessingPolicy? PolicySnapshot { get; set; }
    public int BasedOnVersion { get; set; }
    public List<string> PendingTopics { get; set; } = [];
    public int PendingDirtyRevisions { get; set; }
    public DateTime? PendingDirtySince { get; set; }
}
public sealed record OrganizationDue(int DirtyRevisions, DateTime? DirtySince, DateTime? CooldownUntil, string? Reason, List<string> AffectedTopics);
public static class NoteOrganization
{
    public static IEnumerable<NoteSection> EligibleTopics(IEnumerable<NoteSection> topics) => topics.Where(x => !x.UserEdited && !x.Blocks.Any(b => b.UserEdited));
    public static string Identity(IEnumerable<NoteSection> topics) => ContentHash.Of(JsonSerializer.Serialize(topics.Select(x => new {
        x.Id, x.Title, x.ParentId, x.Order, x.UserEdited, bodies = x.Blocks.Select(b => new {b.Id, b.Order, b.Role, b.Body, b.UserEdited, b.SupersedesBlockId})
    })));
    public static OrganizationDue Due(Note note, NoteOrganizationState state, ProcessingPolicy policy, DateTime now)
    {
        var dirty = Math.Max(state.PendingTopics.Count > 0 ? state.PendingDirtyRevisions : 0, Math.Max(0, note.MeaningfulRevision - state.OrganizedMeaningfulRevision));
        var changes = note.RecentChanges.Where(x => x.Revision > state.OrganizedMeaningfulRevision).ToList();
        DateTime? since = dirty > 0 ? changes.Select(x => (DateTime?)x.At).Append(state.PendingTopics.Count > 0 ? state.PendingDirtySince : null).Where(x => x is not null).Min() ?? note.CreatedAt : null;
        var cooldown = state.LastOrganizedAt?.AddSeconds(policy.OrganizationCooldownSeconds);
        var reason = !policy.OrganizationEnabled || dirty == 0 || cooldown > now ? null :
            dirty >= policy.OrganizationChangedRevisions ? "changed-revisions" : now >= since!.Value.AddSeconds(policy.OrganizationMaxDirtyAgeSeconds) ? "dirty-age" : null;
        return new(dirty, since, cooldown, reason, state.PendingTopics.Concat(changes.SelectMany(x => x.TopicIds)).Distinct().ToList());
    }
}
