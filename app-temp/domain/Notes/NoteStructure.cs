namespace Playback.Domain.Notes;

// Notes should read like nested folders, not one long flat list. Six is where structure is looked at again, not a
// limit: a topic with more than ReviewBlocks blocks, or a level with more than ReviewTopics topics, gets one
// organizer review, which may split, group, merge, rename or reorder, or leave it as it is. While writing, a topic
// that has ReviewBlocks blocks or has run for MaxTopicMinutes of lecture is closed: new speech starts a new topic or
// a subtopic instead of growing it. Depth is a real limit: at most MaxDepth levels (topic, subtopic, sub-subtopic).
public static class NoteStructure
{
    public const int ReviewBlocks = 6;
    public const int ReviewTopics = 6;
    public const int MaxTopicMinutes = 8;
    public const int MaxDepth = 3;
    // A new subtopic goes under a topic still being discussed: one spoken within this many minutes of the new speech.
    public const int MaxParentGapMinutes = 5;

    public static int Depth(NoteSection topic, IReadOnlyCollection<NoteSection> topics)
    {
        var depth = 1;
        for (var parent = topic.ParentId; parent is not null && depth <= MaxDepth; depth++)
            parent = topics.FirstOrDefault(x => x.Id == parent)?.ParentId;
        return depth;
    }

    public static bool IsWithin(NoteSection topic, string ancestorId, IReadOnlyCollection<NoteSection> topics)
    {
        for (var parent = topic.ParentId; parent is not null; parent = topics.FirstOrDefault(x => x.Id == parent)?.ParentId)
            if (parent == ancestorId) return true;
        return false;
    }

    // Topics over the block threshold, at any depth. Above MaxDepth one may get subtopics; at MaxDepth it is reviewed
    // with its parent, so some of its blocks may move into a new or existing sibling.
    public static IEnumerable<NoteSection> Overfull(IReadOnlyCollection<NoteSection> topics) =>
        topics.Where(x => x.Blocks.Count > ReviewBlocks);

    // The topic a review of this topic starts from: itself, or its parent when it sits at MaxDepth.
    public static string ReviewRoot(NoteSection topic, IReadOnlyCollection<NoteSection> topics) =>
        Depth(topic, topics) >= MaxDepth && topic.ParentId is { } parent ? parent : topic.Id;

    // A level over the topic threshold (ParentId null is the top level). Above MaxDepth it can be grouped under new
    // parents from the outline; at MaxDepth no parent fits, so its topics are reviewed with their blocks (merge,
    // rename, reorder or leave). Identity changes when a topic joins or leaves the level, not when blocks change.
    public sealed record CrowdedLevel(string? ParentId, List<string> TopicIds, bool AtMaxDepth)
    {
        public string Identity => ContentHash.Of((ParentId ?? "") + ":" + string.Join(",", TopicIds));
    }

    public static List<CrowdedLevel> CrowdedLevels(IReadOnlyCollection<NoteSection> topics) =>
        topics.GroupBy(x => x.ParentId).Where(x => x.Count() > ReviewTopics)
            .Select(x => new CrowdedLevel(x.Key, x.Select(t => t.Id).Order(StringComparer.Ordinal).ToList(), Depth(x.First(), topics) >= MaxDepth)).ToList();

    // Parents of the levels an outline regroup can group ("" in the input for the top level).
    public static List<string?> CrowdedParents(IReadOnlyCollection<NoteSection> topics) =>
        CrowdedLevels(topics).Where(x => !x.AtMaxDepth).Select(x => x.ParentId).ToList();

    // How many levels lie below a topic: 0 for a topic without subtopics.
    public static int LevelsBelow(NoteSection topic, IReadOnlyCollection<NoteSection> topics, int guard = 0) =>
        guard > MaxDepth ? 0 : topics.Where(x => x.ParentId == topic.Id).Select(x => 1 + LevelsBelow(x, topics, guard + 1)).DefaultIfEmpty(0).Max();

    // firstSpokenMs: when the topic's earliest source was spoken; nowMs: when the newest speech was spoken.
    public static bool IsClosed(NoteSection topic, long? firstSpokenMs, long nowMs) =>
        topic.Blocks.Count >= ReviewBlocks || firstSpokenMs is { } first && nowMs - first >= MaxTopicMinutes * 60_000L;
}
