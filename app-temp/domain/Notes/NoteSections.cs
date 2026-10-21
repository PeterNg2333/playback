using System.Text;
using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

// Notes are one versioned document. Sections, points and citations are part of that document,
// not separate agent-owned copies. Unselected sections never pass through generation.
public static class NoteSections
{
    public static string Render(IEnumerable<NoteSection> sections) => sections.Any(x => x.Blocks.Count > 0 || x.ParentId is not null)
        ? NoteTree.Render(sections, false) : string.Join("\n\n", sections.Select(x => x.Markdown.Trim())).Trim();
    public static List<string> Sources(string markdown, IEnumerable<string> allowed) {
        var ids = allowed.ToHashSet(StringComparer.Ordinal);
        return CitationSyntax.Bodies(markdown).SelectMany(x => x.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(ids.Contains).Distinct(StringComparer.Ordinal).ToList();
    }
    public static List<NoteCitation> CloneCitations(IEnumerable<NoteCitation> citations) => citations
        .Select(x => new NoteCitation { Id = x.Id, SourceIds = x.SourceIds.ToList() }).ToList();
    public static List<NoteCitation> UsedCitations(IEnumerable<NoteSection> sections, List<NoteCitation> citations) {
        var ids = sections.SelectMany(s => CitationSyntax.Bodies(s.Markdown).Concat(s.Points.SelectMany(p => CitationSyntax.Bodies(p.Text))))
            .ToHashSet(StringComparer.Ordinal);
        return citations.Where(x => ids.Contains(x.Id)).ToList();
    }
    public static string Compact(string sessionId, string markdown, IEnumerable<string> allowed, List<NoteCitation> citations) {
        var ids = allowed.ToHashSet(StringComparer.Ordinal);
        return Regex.Replace(markdown, @"(?<code>```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\r\n]*`)|\[(?<body>[^\[\]\r\n]{1,16000})\](?!\()", match => {
            if (match.Groups["code"].Success) return match.Value;
            var sources = match.Groups["body"].Value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (sources.Length == 0 || !sources.All(ids.Contains)) return match.Value;
            var sorted = sources.Distinct().Order(StringComparer.Ordinal).ToList();
            var key = "cite_" + ContentHash.Of(sessionId + "\n" + string.Join('\n', sorted))[..20];
            if (!citations.Any(x => x.Id == key)) citations.Add(new NoteCitation { Id = key, SourceIds = sorted });
            return "[" + key + "]";
        });
    }
    public static string Expand(string markdown, IEnumerable<NoteCitation> citations) {
        var map = citations.ToDictionary(x => x.Id, x => string.Join(", ", x.SourceIds));
        return Regex.Replace(markdown, @"\[(cite_[a-f0-9]{20})\]", m => map.TryGetValue(m.Groups[1].Value, out var ids) ? "[" + ids + "]" : m.Value);
    }
    static IEnumerable<string> Blocks(string markdown, bool subtopics = false) {
        var current = new StringBuilder(); var fenced = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n')) {
            if (Regex.IsMatch(line, @"^\s*(```|~~~)")) fenced = !fenced;
            if (!fenced && Regex.IsMatch(line, subtopics ? @"^#{2,4}\s+" : @"^##\s+") && current.Length > 0) {
                yield return current.ToString().Trim(); current.Clear();
            }
            current.Append(line).Append('\n');
        }
        if (current.ToString().Trim().Length > 0) yield return current.ToString().Trim();
    }
    // The current note as sections; versions saved before sections existed are split by heading.
    public static List<NoteSection> Read(Note? note, IEnumerable<string> allowed) {
        if (note is null) return [];
        if (note.Sections.Count > 0) return note.Sections;
        return Blocks(note.Markdown).Select((block, i) => {
            var title = Regex.Match(block, @"(?m)^#{1,3}\s+(.+)$").Groups[1].Value;
            return new NoteSection { Id = "section_" + ContentHash.Of(note.SessionId + "\n" + note.Version + "\n" + i + "\n" + block)[..20],
                Title = title.Length > 0 ? title : "Notes", Markdown = block, UserEdited = note.Author.StartsWith("user"),
                Points = LegacyPoints(note.SessionId, block, allowed) };
        }).ToList();
    }
    static List<NotePoint> LegacyPoints(string sessionId, string markdown, IEnumerable<string> allowed) =>
        Regex.Split(markdown.Trim(), @"\n\s*\n").Where(x => !Regex.IsMatch(x.Trim(), @"^#{1,6}\s+[^\n]+$") && x.Trim().Length > 0)
        .Select((text, i) => new NotePoint { Id = "point_" + ContentHash.Of(sessionId + "\n" + i + "\n" + text)[..20],
            Text = text.Trim(), SourceIds = Sources(text, allowed) }).ToList();

    // Match actual unchanged bodies and their source overlap, not heading equality or word counts.
    // An edited section is protected from automatic updates. Removal records durable tombstones.
    public static List<NoteSection> UserEdit(Note? previous, string sessionId, string markdown, IEnumerable<string> allowed,
        List<NoteCitation> citations, List<string> deleted, List<string> suppressed) {
        var old = Read(previous, allowed).ToList(); var result = new List<NoteSection>();
        if (previous?.SchemaVersion == 2 && previous.Markdown.Replace("\r\n", "\n").Trim() == markdown.Replace("\r\n", "\n").Trim())
            return old.Select(x => NoteTree.Canonical(x)).ToList();
        var parents = new Dictionary<int, string>();
        string NormalizeHeading(string text) => Regex.Replace(text, @"\A#{1,4}\s+(?:\d+(?:\.\d+)*\s+)?", "## ");
        foreach (var block in Blocks(markdown, previous?.SchemaVersion == 2)) {
            var expanded = Expand(block, citations);
            var exact = old.FirstOrDefault(x => NormalizeHeading(Expand(x.Markdown, citations)).Replace("\r\n", "\n").Trim() == NormalizeHeading(expanded).Trim());
            var sources = Sources(expanded, allowed);
            var match = exact ?? old.Select(x => new { Section = x, Score = Similarity(Expand(x.Markdown, citations), expanded) +
                    x.Points.SelectMany(p => p.SourceIds).Intersect(sources).Count() * 2 })
                .OrderByDescending(x => x.Score).FirstOrDefault(x => x.Score > 0)?.Section;
            if (match is not null) old.Remove(match);
            var heading = Regex.Match(block, @"\A(?<hash>#{1,4})\s+");
            var depth = previous?.SchemaVersion == 2 ? Math.Max(1, heading.Groups["hash"].Length - 1) : 1;
            var parent = depth > 1 ? parents.GetValueOrDefault(depth - 1) : null;
            if (depth > 1 && parent is null) throw new InvalidOperationException("A subtopic requires a preceding parent topic");
            if (exact is not null) { var retained = NoteTree.Canonical(exact); retained.ParentId = parent; retained.Order = result.Count; result.Add(retained); parents[depth] = retained.Id; continue; }
            var compact = Compact(sessionId, expanded, allowed, citations);
            result.Add(new NoteSection { Id = match?.Id ?? "section_" + Guid.NewGuid().ToString("N"), Version = (match?.Version ?? 0) + 1,
                Title = Regex.Replace(Regex.Match(block, @"(?m)^#{1,4}\s+(.+)$").Groups[1].Value.Trim(), @"^\d+(?:\.\d+)*\s+", ""), ParentId = parent, Order = result.Count, Markdown = compact, UserEdited = true,
                Points = LegacyPoints(sessionId, expanded, allowed) });
            parents[depth] = result[^1].Id;
            if (match is not null) suppressed.AddRange(match.Points.SelectMany(x => x.SourceIds).Except(sources));
        }
        foreach (var removed in old) { deleted.Add(removed.Id); suppressed.AddRange(removed.Points.SelectMany(x => x.SourceIds)); }
        return result;
    }
    static int Similarity(string a, string b) {
        var left = a.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 12 && !x.StartsWith('#')).ToHashSet();
        return b.Split('\n').Count(x => left.Contains(x.Trim()));
    }


}
// Segments, Summary and ReviewQuestions are kept from the previous version when null.
public sealed record SectionUpdate(List<NoteSection> Sections, List<NoteCitation> Citations, List<SourceDisposition> Coverage, string? Presentation = null,
    List<NoteSegment>? Segments = null, List<string>? Summary = null, List<string>? ReviewQuestions = null);
