using System.Text.RegularExpressions;
using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// How the topic tree is laid out from what the three speeds wrote. Topic IDs say where a topic came from:
//   topic-…    finished by the final pass (outline + polish); kept as they are until a segment they cite is rewritten;
//   segment-N  a segment not yet finished, shown as one topic in lecture order;
//   live       this recording's jots, one block per minute (live-M), removed once their window's segment is written.
// A block the owner edited or locked is never replaced: it keeps its text wherever its topic goes.
public static class NoteLayout
{
    public const string LiveTopicId = "live";
    public static string LiveBlockId(int minute) => "live-" + minute;
    public static string DraftTopicId(int segment) => "segment-" + segment;
    public static bool IsFinished(NoteSection topic) => topic.Id.StartsWith("topic-", StringComparison.Ordinal);

    public static string SegmentBlockId(int segment, string hash) => $"s{segment}-{hash[..12]}";
    public static int? SegmentOf(string blockId) => Regex.Match(blockId, @"^s(\d+)-") is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;
    public static int? MinuteOf(string blockId) => Regex.Match(blockId, @"^live-(\d+)$") is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    // A block that names its sources, so citations, lineage and the note's source list see them. A body that cites none of
    // them gets them appended as one citation.
    public static NoteBlock Block(string id, string role, string body, IEnumerable<string> sourceIds, int order = 0)
    {
        var sources = sourceIds.Distinct(StringComparer.Ordinal).ToList();
        if (sources.Count > 0 && CitationSyntax.Bodies(body).All(x => !x.Split(',').Any(cited => sources.Contains(cited.Trim()))))
            body = body.TrimEnd() + " [" + string.Join(", ", sources) + "]";
        return new NoteBlock { Id = id, Role = role, Order = order, Body = body, SourceIds = sources };
    }

    // The sources a saved block cites, through the note's compacted citations.
    public static List<string> Sources(NoteBlock block, IEnumerable<NoteCitation> citations) =>
        block.SourceIds.Concat(CitationSyntax.Bodies(NoteSections.Expand(block.Body, citations))
            .SelectMany(x => x.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(x => !x.StartsWith("ref:", StringComparison.Ordinal)))
        .Distinct(StringComparer.Ordinal).ToList();

    public static List<NoteBlock> Edited(IEnumerable<NoteSection> topics) => topics.SelectMany(x => x.Blocks).Where(x => x.UserEdited).ToList();

    // Finished topics as they are, then a draft topic for every segment no finished topic was polished from, then the live
    // jots. A draft keeps the owner's edits to its blocks and any edited block that is no longer in the segment.
    public static List<NoteSection> Arrange(Note? previous, IReadOnlyList<NoteSegment> segments, IReadOnlyList<NoteBlock> live, string language)
    {
        var old = previous?.Sections.Select(NoteTree.Clone).ToList() ?? [];
        var finished = old.Where(IsFinished).ToList();
        var placed = finished.SelectMany(x => x.SegmentBlockIds).Select(SegmentOf).OfType<int>().ToHashSet();
        var order = finished.Where(x => x.ParentId is null).Select(x => x.Order + 1).DefaultIfEmpty(0).Max();
        var result = new List<NoteSection>(finished);
        foreach (var segment in segments.Where(x => !placed.Contains(x.Index)).OrderBy(x => x.Index))
        {
            var draft = old.FirstOrDefault(x => x.Id == DraftTopicId(segment.Index));
            var edited = draft?.Blocks.Where(x => x.UserEdited).ToDictionary(x => x.Id) ?? [];
            var blocks = segment.Blocks.Select(b => edited.GetValueOrDefault(b.Id) ?? b)
                .Concat(edited.Values.Where(e => segment.Blocks.All(b => b.Id != e.Id))).ToList();
            for (var i = 0; i < blocks.Count; i++) blocks[i].Order = i;
            if (blocks.Count == 0) continue;
            result.Add(new NoteSection { Id = DraftTopicId(segment.Index), Order = order++,
                Title = draft?.UserEdited == true ? draft.Title : segment.Title, UserEdited = draft?.UserEdited ?? false, Blocks = blocks });
        }
        if (live.Count > 0)
            result.Add(new NoteSection { Id = LiveTopicId, Order = order, Title = NotePrompts.LiveTitle(language),
                Blocks = live.OrderBy(x => MinuteOf(x.Id) ?? int.MaxValue).Select((x, i) => { x.Order = i; return x; }).ToList() });
        return result;
    }

    public static List<NoteBlock> Live(Note? note) =>
        note?.Sections.FirstOrDefault(x => x.Id == LiveTopicId) is { } topic ? NoteTree.Clone(topic).Blocks : [];

    // Every block placed once, parents that exist, at most three levels; a block the outline missed joins the topic of
    // the block before it, and empty topics are dropped. Keys are the model's; the caller gives the topics real IDs.
    public sealed record OutlineTopic(string Key, string Title, string Parent, List<string> Blocks);
    public static List<OutlineTopic> RepairOutline(IEnumerable<OutlineTopic> raw, IReadOnlyList<string> blockIds)
    {
        var topics = new List<OutlineTopic>();
        foreach (var t in raw)
            if (t.Key.Length > 0 && topics.All(x => x.Key != t.Key)) topics.Add(t with { Title = t.Title.Trim(), Blocks = [] });
        if (topics.Count == 0) topics.Add(new("k0", "Notes", "", []));
        string Parent(OutlineTopic t) => topics.Any(x => x.Key == t.Parent && x.Key != t.Key) ? t.Parent : "";
        topics = topics.Select(t => t with { Parent = Parent(t) }).ToList();
        int Depth(OutlineTopic t)
        {
            var depth = 1;
            for (var p = t.Parent; p.Length > 0 && depth <= topics.Count; p = topics.First(x => x.Key == p).Parent) depth++;
            return depth;
        }
        for (var i = 0; i < topics.Count; i++)
        {
            if (Depth(topics[i]) > topics.Count) topics[i] = topics[i] with { Parent = "" }; // a parent cycle
            while (Depth(topics[i]) > 3) topics[i] = topics[i] with { Parent = topics.First(x => x.Key == topics[i].Parent).Parent };
        }
        var placed = new Dictionary<string, string>();
        foreach (var t in raw)
            foreach (var id in t.Blocks)
                if (topics.Any(x => x.Key == t.Key) && blockIds.Contains(id) && !placed.ContainsKey(id)) placed[id] = t.Key;
        var last = topics[0].Key;
        foreach (var id in blockIds)
        {
            if (!placed.ContainsKey(id)) placed[id] = last;
            last = placed[id];
            topics.First(x => x.Key == placed[id]).Blocks.Add(id);
        }
        var position = blockIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        int First(OutlineTopic t) => t.Blocks.Select(x => position[x]).Concat(topics.Where(c => c.Parent == t.Key).Select(First)).DefaultIfEmpty(int.MaxValue).Min();
        while (topics.RemoveAll(t => t.Blocks.Count == 0 && topics.All(c => c.Parent != t.Key)) > 0) { }
        return topics.OrderBy(First).ToList();
    }

    // Stable for the same session, outline key, title and blocks, so an unchanged finish keeps its topic IDs.
    public static string TopicId(string sessionId, params IEnumerable<string> parts) =>
        "topic-" + ContentHash.Of(sessionId + "\n" + string.Join("\n", parts))[..16];
}
