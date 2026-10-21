using System.Text;
using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

// Citations in note text, and the owner's whole-note Markdown edit.
public static class NoteSections
{
    // The allowed source IDs a text cites, in order.
    public static List<string> Sources(string markdown, IEnumerable<string> allowed)
    {
        var ids = allowed.ToHashSet(StringComparer.Ordinal);
        return CitationSyntax.Bodies(markdown).SelectMany(x => x.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(ids.Contains).Distinct(StringComparer.Ordinal).ToList();
    }

    public static List<NoteCitation> CloneCitations(IEnumerable<NoteCitation> citations) => citations
        .Select(x => new NoteCitation { Id = x.Id, SourceIds = x.SourceIds.ToList() }).ToList();

    // The citations some block still uses; a saved version keeps only these.
    public static List<NoteCitation> UsedCitations(IEnumerable<NoteSection> topics, IEnumerable<NoteCitation> citations)
    {
        var ids = topics.SelectMany(t => t.Blocks).SelectMany(b => CitationSyntax.Bodies(b.Body)).ToHashSet(StringComparer.Ordinal);
        return citations.Where(x => ids.Contains(x.Id)).ToList();
    }

    // [source, source] → [cite_…]: a stable key per session and source set, added to citations. Code and links stay as they are.
    public static string Compact(string sessionId, string markdown, IEnumerable<string> allowed, List<NoteCitation> citations)
    {
        var ids = allowed.ToHashSet(StringComparer.Ordinal);
        return CitationSyntax.Rewrite(markdown, body =>
        {
            var sources = body.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (sources.Length == 0 || !sources.All(ids.Contains)) return body;
            var sorted = sources.Distinct().Order(StringComparer.Ordinal).ToList();
            var key = "cite_" + ContentHash.Of(sessionId + "\n" + string.Join('\n', sorted))[..20];
            if (!citations.Any(x => x.Id == key)) citations.Add(new NoteCitation { Id = key, SourceIds = sorted });
            return key;
        });
    }

    // [cite_…] → [source, source].
    public static string Expand(string markdown, IEnumerable<NoteCitation> citations)
    {
        var map = citations.ToDictionary(x => x.Id, x => string.Join(", ", x.SourceIds));
        return Regex.Replace(markdown, @"\[(cite_[a-f0-9]{20})\]", m => map.TryGetValue(m.Groups[1].Value, out var ids) ? "[" + ids + "]" : m.Value);
    }

    // The owner saved the whole notes as Markdown. Each topic is matched to a previous one by its title and by the blocks
    // whose text is still there unchanged; those blocks keep their identity, sources and lock, and new or changed text
    // becomes the owner's own (locked) blocks. A heading's depth gives its parent. Topics left out are removed.
    public static List<NoteSection> UserEdit(Note? previous, string sessionId, string markdown, IEnumerable<string> allowed, List<NoteCitation> citations)
    {
        var old = previous?.Sections.Select(NoteTree.Clone).ToList() ?? [];
        if (previous is not null && Normalize(previous.Markdown) == Normalize(markdown)) return old;
        var result = new List<NoteSection>();
        var parents = new Dictionary<int, string>();
        foreach (var (depth, title, body) in Topics(markdown))
        {
            var parent = depth > 1 ? parents.GetValueOrDefault(depth - 1) : null;
            if (depth > 1 && parent is null) throw new InvalidOperationException("A subtopic requires a preceding parent topic");
            var text = Expand(body, citations);
            NoteSection? match = old.Select(x => (Topic: x, Score: (x.Title == title ? 1 : 0) +
                    2 * x.Blocks.Count(b => text.Contains(Expand(b.Body, citations), StringComparison.Ordinal))))
                .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.Topic).FirstOrDefault();
            if (match is not null) old.Remove(match);
            var topic = match ?? new NoteSection { Id = "section_" + Guid.NewGuid().ToString("N"), Version = 0, UserEdited = true };
            var blocks = Split(sessionId, topic, text, allowed, citations);
            if (topic.Title != title || !blocks.Select(x => x.Id).SequenceEqual(topic.Blocks.OrderBy(x => x.Order).Select(x => x.Id))) topic.Version++;
            if (match is not null && match.Title != title) topic.UserEdited = true;
            topic.Title = title;
            topic.ParentId = parent;
            topic.Order = result.Count(x => x.ParentId == parent);
            topic.Blocks = blocks;
            result.Add(topic);
            parents[depth] = topic.Id;
        }
        return result;
    }

    // The topics in saved Markdown: heading depth (## is 1), the title without its number ("1.2 "), and the body.
    // Text before the first heading becomes a topic titled "Notes".
    static List<(int Depth, string Title, string Body)> Topics(string markdown)
    {
        var topics = new List<(int, string, string)>();
        var depth = 1;
        string? title = null;
        var body = new StringBuilder();
        var fenced = false;
        void Flush()
        {
            if (title is not null || body.ToString().Trim().Length > 0) topics.Add((depth, title ?? "Notes", body.ToString().Trim()));
            body.Clear();
        }
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (Regex.IsMatch(line, @"^\s*(```|~~~)")) fenced = !fenced;
            var heading = fenced ? Match.Empty : Regex.Match(line, @"^(#{2,4})\s+(?:\d+(?:\.\d+)*\s+)?(.+?)\s*$");
            if (heading.Success)
            {
                Flush();
                depth = heading.Groups[1].Length - 1;
                title = heading.Groups[2].Value;
                continue;
            }
            body.Append(line).Append('\n');
        }
        Flush();
        return topics;
    }

    // The previous blocks still present verbatim, in order, keep their identity; the text between them becomes new blocks.
    static List<NoteBlock> Split(string sessionId, NoteSection topic, string body, IEnumerable<string> allowed, List<NoteCitation> citations)
    {
        var result = new List<NoteBlock>();
        var cursor = 0;
        void Add(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return;
            result.Add(new NoteBlock { Id = "block_" + ContentHash.Of(topic.Id + ":" + result.Count + ":" + text)[..20], UserEdited = true,
                Body = Compact(sessionId, text, allowed, citations), SourceIds = Sources(text, allowed) });
        }
        foreach (var block in topic.Blocks.OrderBy(x => x.Order))
        {
            var text = Expand(block.Body, citations);
            var at = body.IndexOf(text, cursor, StringComparison.Ordinal);
            if (at < 0) continue;
            Add(body[cursor..at]);
            result.Add(NoteTree.Clone(block));
            cursor = at + text.Length;
        }
        Add(body[cursor..]);
        for (var i = 0; i < result.Count; i++) result[i].Order = i;
        return result;
    }

    static string Normalize(string markdown) => markdown.Replace("\r\n", "\n").Trim();
}

// What a writer saves. Segments, Summary and ReviewQuestions are kept from the previous version when null.
public sealed record SectionUpdate(List<NoteSection> Sections, List<NoteCitation> Citations,
    List<NoteSegment>? Segments = null, List<string>? Summary = null, List<string>? ReviewQuestions = null);
