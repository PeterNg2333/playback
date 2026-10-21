namespace Playback.Domain.Notes;

// The topic tree: copying, numbering, rendering and the limits every saved version keeps.
public static class NoteTree
{
    const int MaxTopics = 512;
    const int MaxDepth = 3;
    const int MaxTitleLength = 200;
    const int MaxBlockLength = 250_000;

    // A deep copy, so a caller can change topics and blocks without touching the version it read them from.
    public static NoteSection Clone(NoteSection topic) => new() { Id = topic.Id, Version = topic.Version, Title = topic.Title,
        ParentId = topic.ParentId, Order = topic.Order, UserEdited = topic.UserEdited, SegmentBlockIds = topic.SegmentBlockIds.ToList(),
        Blocks = topic.Blocks.Select(Clone).ToList() };
    public static NoteBlock Clone(NoteBlock block) => new() { Id = block.Id, Order = block.Order, Role = block.Role, Body = block.Body,
        UserEdited = block.UserEdited, CitationIds = block.CitationIds.ToList(), SourceIds = block.SourceIds.ToList() };

    // Writes inline source lists as compact citation keys, adding the keys to citations.
    public static List<NoteBlock> Compact(string sessionId, IEnumerable<NoteBlock> blocks, IEnumerable<string> allowed, List<NoteCitation> citations) =>
        blocks.Select(original =>
        {
            var block = Clone(original);
            block.Body = NoteSections.Compact(sessionId, original.Body, allowed, citations);
            block.CitationIds = CitationSyntax.Bodies(block.Body).Distinct().ToList();
            return block;
        }).ToList();

    // Topics in reading order with their number ("1", "1.2", …) and depth (1–3).
    public static List<(NoteSection Topic, string Number, int Depth)> Outline(IEnumerable<NoteSection> topics)
    {
        var list = topics.ToList();
        Validate(list);
        var result = new List<(NoteSection, string, int)>();
        void Visit(string? parent, string prefix, int depth)
        {
            var i = 0;
            foreach (var topic in list.Where(x => x.ParentId == parent).OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var number = prefix + ++i;
                result.Add((topic, number, depth));
                Visit(topic.Id, number + ".", depth + 1);
            }
        }
        Visit(null, "", 1);
        return result;
    }

    // The saved Markdown: "## 1 Title", "### 1.1 Subtopic", then each topic's blocks.
    public static string Render(IEnumerable<NoteSection> topics) => string.Join("\n\n", Outline(topics).Select(row =>
        new string('#', row.Depth + 1) + " " + row.Number + " " + row.Topic.Title + "\n\n" +
        string.Join("\n\n", row.Topic.Blocks.OrderBy(x => x.Order).Select(x => x.Body)))).Trim();

    public static void Validate(IReadOnlyList<NoteSection> topics)
    {
        if (topics.Count > MaxTopics || topics.Select(x => x.Id).Distinct().Count() != topics.Count)
            throw new InvalidOperationException("Duplicate topics or topic budget exceeded");
        var known = topics.ToDictionary(x => x.Id);
        var blocks = new HashSet<string>();
        foreach (var topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic.Id)) throw new InvalidOperationException("Missing topic identity");
            if (topic.Order < 0 || topic.Title.Length > MaxTitleLength) throw new InvalidOperationException("Invalid topic order/title");
            var seen = new HashSet<string> { topic.Id };
            for (var parent = topic.ParentId; parent is not null; parent = known[parent].ParentId)
                if (!known.ContainsKey(parent) || !seen.Add(parent) || seen.Count > MaxDepth)
                    throw new InvalidOperationException("Unknown parent, cycle or topic depth exceeds three");
            foreach (var block in topic.Blocks)
                if (!blocks.Add(block.Id) || block.Body.Length is < 1 or > MaxBlockLength || block.Order < 0)
                    throw new InvalidOperationException("Invalid block identity/body");
        }
    }
}
