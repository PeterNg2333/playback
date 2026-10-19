namespace Playback.Domain.Notes;

// Notes should read like nested folders, not one long flat list. A topic holding more than MaxBlocksPerTopic
// blocks needs subtopics; a level holding more than MaxTopicsPerLevel topics needs grouping under parents.
// A topic that is full or has run for MaxTopicMinutes of lecture is closed: new speech starts a new topic or a
// subtopic instead of growing it. Depth stays at most MaxDepth (topic, subtopic, sub-subtopic).
public static class NoteStructure
{
    public const int MaxBlocksPerTopic = 6;
    public const int MaxTopicsPerLevel = 6;
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

    // Topics that need subtopics and can still have them.
    public static IEnumerable<NoteSection> Overfull(IReadOnlyCollection<NoteSection> topics) =>
        topics.Where(x => x.Blocks.Count > MaxBlocksPerTopic && Depth(x, topics) < MaxDepth);

    public static bool HasCrowdedLevel(IReadOnlyCollection<NoteSection> topics) =>
        topics.GroupBy(x => x.ParentId).Any(x => x.Count() > MaxTopicsPerLevel);

    // firstSpokenMs: when the topic's earliest source was spoken; nowMs: when the newest speech was spoken.
    public static bool IsClosed(NoteSection topic, long? firstSpokenMs, long nowMs) =>
        topic.Blocks.Count >= MaxBlocksPerTopic || firstSpokenMs is { } first && nowMs - first >= MaxTopicMinutes * 60_000L;
}
