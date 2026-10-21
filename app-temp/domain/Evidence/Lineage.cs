using Playback.Domain.Models;

namespace Playback.Domain.Evidence;

// Source lineage: audio chunk → transcript segment (a rerun window supersedes the rows it re-recognized) → note
// block → topic, and term → segment. Stored notes keep the IDs they were written with; every read maps a superseded
// ID to the segment that replaced it, so the next saved version carries current IDs without a rewrite per window.
public static class Lineage
{
    // Old ID → current active ID, following chains (a window can itself be superseded by a later rerun).
    public static Dictionary<string, string> Supersessions(IEnumerable<Transcript> rows)
    {
        var direct = rows.Where(x => x.SupersededBy is not null).ToDictionary(x => x.Id, x => x.SupersededBy!, StringComparer.Ordinal);
        return direct.Keys.ToDictionary(x => x, x => Resolve(direct, x), StringComparer.Ordinal);
    }

    public static string Resolve(IReadOnlyDictionary<string, string> map, string id)
    {
        var current = id;
        for (var i = 0; i < 32 && map.TryGetValue(current, out var next); i++) current = next;
        return current;
    }

    public static List<string> Remap(IEnumerable<string> ids, IReadOnlyDictionary<string, string> map) =>
        ids.Select(x => Resolve(map, x)).Distinct(StringComparer.Ordinal).ToList();

    // Citations and block source lists move to the replacing segment.
    public static Note Apply(Note note, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return note;
        foreach (var citation in note.Citations) citation.SourceIds = Remap(citation.SourceIds, map);
        foreach (var block in note.Sections.SelectMany(x => x.Blocks))
        {
            block.Body = Body(block.Body, map);
            block.SourceIds = Remap(block.SourceIds, map);
        }
        note.Markdown = Body(note.Markdown, map);
        note.TranscriptIds = Remap(note.TranscriptIds, map);
        foreach (var edit in note.Edits) edit.TranscriptIds = Remap(edit.TranscriptIds, map);
        return note;
    }

    // Raw source IDs written into a body are mapped too; cite_ keys keep their ID and
    // their citation's source list moves instead, so block bodies stay byte-identical.
    static string Body(string text, IReadOnlyDictionary<string, string> map) => string.IsNullOrEmpty(text) ? text : CitationSyntax.Rewrite(text, body =>
    {
        if (body.StartsWith("ref:", StringComparison.OrdinalIgnoreCase)) return body;
        var tokens = body.Split([',', ';'], StringSplitOptions.TrimEntries);
        return tokens.Any(map.ContainsKey) ? string.Join(", ", Remap(tokens, map)) : body;
    });
}

public sealed record LineageNode(string Id, string Kind, string Label, long? StartMs = null, long? EndMs = null, bool Active = true, string? Detail = null);
public sealed record LineageEdge(string From, string To, string Kind);
public sealed record LineageView(List<LineageNode> Nodes, List<LineageEdge> Edges, bool Truncated);

// The deterministic trace graph. Edges: covers (segment → chunk), supersedes (new → old segment),
// cites (block → segment), contains (topic → block), parent (topic → subtopic), mentions (term → segment).
public static class LineageGraph
{
    public const int MaxNodes = 600;

    public static LineageView Build(IEnumerable<ChunkRecord> chunks, IEnumerable<Transcript> transcripts, Note? note,
        IEnumerable<TermInsight> insights, string? focus = null, int depth = 2)
    {
        var nodes = new Dictionary<string, LineageNode>(StringComparer.Ordinal);
        var edges = new List<LineageEdge>();
        var rows = transcripts.ToList();
        var map = Lineage.Supersessions(rows);
        foreach (var chunk in chunks) nodes[chunk.Id] = new(chunk.Id, "chunk", $"{chunk.SourceId} #{chunk.Sequence}", chunk.StartMs, chunk.EndMs, Detail: chunk.Status);
        foreach (var row in rows)
        {
            nodes[row.Id] = new(row.Id, "segment", Excerpt(row.SourceText), row.StartMs, row.EndMs, row.Active,
                row.Kind == "window" ? $"window · {row.CoveredChunkIds.Count} chunks · {row.AsrModel}" : row.AsrModel);
            foreach (var chunk in row.CoveredChunkIds) edges.Add(new(row.Id, chunk, "covers"));
            if (row.SupersededBy is not null) edges.Add(new(row.SupersededBy, row.Id, "supersedes"));
        }
        if (note is not null)
        {
            var citations = note.Citations.ToDictionary(x => x.Id, x => x.SourceIds, StringComparer.Ordinal);
            foreach (var topic in note.Sections)
            {
                nodes[topic.Id] = new(topic.Id, "topic", topic.Title);
                if (topic.ParentId is not null) edges.Add(new(topic.ParentId, topic.Id, "parent"));
                foreach (var block in topic.Blocks)
                {
                    nodes[block.Id] = new(block.Id, "block", Excerpt(block.Body), Detail: block.Role);
                    edges.Add(new(topic.Id, block.Id, "contains"));
                    var cited = CitationSyntax.Bodies(block.Body)
                        .SelectMany(x => citations.TryGetValue(x, out var ids) ? ids : (IEnumerable<string>)x.Split([',', ';'], StringSplitOptions.TrimEntries))
                        .Concat(block.SourceIds)
                        .Select(x => Lineage.Resolve(map, x)).Distinct(StringComparer.Ordinal);
                    foreach (var source in cited.Where(nodes.ContainsKey)) edges.Add(new(block.Id, source, "cites"));
                }
            }
        }
        foreach (var insight in insights)
        {
            var id = "term:" + insight.Id;
            nodes[id] = new(id, "term", insight.Term);
            foreach (var source in Lineage.Remap(insight.TranscriptIds, map).Where(nodes.ContainsKey)) edges.Add(new(id, source, "mentions"));
        }
        edges = edges.Where(x => nodes.ContainsKey(x.From) && nodes.ContainsKey(x.To)).Distinct().ToList();
        if (focus is null)
            return nodes.Count <= MaxNodes ? new(nodes.Values.ToList(), edges, false)
                : Keep(nodes, edges, nodes.Values.Where(x => x.Kind is "topic" or "block" || x.Kind == "segment" && x.Active).Take(MaxNodes).Select(x => x.Id).ToHashSet());
        if (!nodes.ContainsKey(focus)) throw new InvalidOperationException("Lineage node is not in this session");
        var adjacent = edges.SelectMany(x => new[] { (x.From, x.To), (x.To, x.From) }).ToLookup(x => x.Item1, x => x.Item2);
        var seen = new HashSet<string>(StringComparer.Ordinal) { focus };
        var frontier = new List<string> { focus };
        for (var level = 0; level < Math.Clamp(depth, 1, 4) && seen.Count < MaxNodes; level++)
            frontier = frontier.SelectMany(x => adjacent[x]).Where(seen.Add).Take(MaxNodes - seen.Count + 1).ToList();
        return Keep(nodes, edges, seen);
    }

    static LineageView Keep(Dictionary<string, LineageNode> nodes, List<LineageEdge> edges, HashSet<string> keep) =>
        new(nodes.Values.Where(x => keep.Contains(x.Id)).ToList(), edges.Where(x => keep.Contains(x.From) && keep.Contains(x.To)).ToList(), keep.Count < nodes.Count);

    static string Excerpt(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
