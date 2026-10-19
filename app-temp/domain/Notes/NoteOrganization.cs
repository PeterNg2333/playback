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
    // Structures an organizer review has seen and left over the threshold: crowded levels
    // (NoteStructure.CrowdedLevel.Identity) and full topics (NoteStructure.ReviewIdentity). They are not asked again
    // until a topic joins or leaves the level, or a block joins or leaves the topic. The most recent few are kept.
    public List<string> ReviewedStructures { get; set; } = [];
}
public sealed record OrganizationDue(int DirtyRevisions, DateTime? DirtySince, DateTime? CooldownUntil, string? Reason, List<string> AffectedTopics);
public static class NoteOrganization
{
    public static IEnumerable<NoteSection> EligibleTopics(IEnumerable<NoteSection> topics) => topics.Where(x => !x.UserEdited && !x.Blocks.Any(b => b.UserEdited));
    public static string Identity(IEnumerable<NoteSection> topics) => ContentHash.Of(JsonSerializer.Serialize(topics.Select(x => new {
        x.Id, x.Title, x.ParentId, x.Order, x.UserEdited, bodies = x.Blocks.Select(b => new {b.Id, b.Order, b.Role, b.Body, b.UserEdited, b.SupersedesBlockId})
    })));
    // spoken: when each source was spoken, so a topic full by time counts; without it only block counts do.
    public static OrganizationDue Due(Note note, NoteOrganizationState state, ProcessingPolicy policy, DateTime now, IReadOnlyDictionary<string, long>? spoken = null)
    {
        var dirty = Math.Max(state.PendingTopics.Count > 0 ? state.PendingDirtyRevisions : 0, Math.Max(0, note.MeaningfulRevision - state.OrganizedMeaningfulRevision));
        var changes = note.RecentChanges.Where(x => x.Revision > state.OrganizedMeaningfulRevision).ToList();
        DateTime? since = dirty > 0 ? changes.Select(x => (DateTime?)x.At).Append(state.PendingTopics.Count > 0 ? state.PendingDirtySince : null).Where(x => x is not null).Min() ?? note.CreatedAt : null;
        var cooldown = state.LastOrganizedAt?.AddSeconds(policy.OrganizationCooldownSeconds);
        // Organization follows structure, not time. Past a threshold (six blocks or eight minutes for a topic, six
        // topics for a level) the organizer reviews once, and leaving things as they are is a valid outcome: a changed
        // full topic (it may split it), a level over six topics that can take a parent (grouped from the whole
        // outline), or over six subtopics at the third level (reviewed with their blocks: merge, rename, reorder or
        // leave). An unchanged structure, or one a review already left (ReviewedStructures), is not asked again.
        var changed = state.PendingTopics.Concat(changes.SelectMany(x => x.TopicIds)).ToHashSet();
        var eligible = EligibleTopics(note.Sections).ToList();
        var overfull = NoteStructure.Overfull(note.Sections, spoken)
            .Where(x => changed.Contains(x.Id) && eligible.Contains(x) && !state.ReviewedStructures.Contains(NoteStructure.ReviewIdentity(x))).Select(x => x.Id).ToList();
        var crowded = NoteStructure.CrowdedLevels(note.Sections).Where(x => !state.ReviewedStructures.Contains(x.Identity)).ToList();
        var subtopics = crowded.Where(x => x.AtMaxDepth && x.ParentId is { } parent && eligible.Any(t => t.Id == parent)).Select(x => x.ParentId!).ToList();
        var reason = !policy.OrganizationEnabled || dirty == 0 || cooldown > now ? null :
            overfull.Count > 0 ? "large-topic" : crowded.Any(x => !x.AtMaxDepth) ? "crowded-level" : subtopics.Count > 0 ? "crowded-subtopics" : null;
        return new(dirty, since, cooldown, reason, reason switch { "large-topic" => overfull, "crowded-subtopics" => subtopics, _ => changed.ToList() });
    }

    // After a review: what it covered that is still past the threshold, so it is not asked again until it changes.
    public static List<string> StillOverAfterReview(IReadOnlyCollection<NoteSection> topics, string reason, IReadOnlyCollection<string> affected,
        IReadOnlyDictionary<string, long>? spoken = null) => reason == "large-topic"
        ? NoteStructure.Overfull(topics, spoken).Where(x => affected.Contains(x.Id)).Select(NoteStructure.ReviewIdentity).ToList()
        : NoteStructure.CrowdedLevels(topics).Where(x => reason == "crowded-level" ? !x.AtMaxDepth : reason == "crowded-subtopics" && x.ParentId is { } parent && affected.Contains(parent))
            .Select(x => x.Identity).ToList();
}
