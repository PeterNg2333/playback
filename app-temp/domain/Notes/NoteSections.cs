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

    public static void RequireRetention(Note? previous, SectionUpdate update, IEnumerable<string> allowedIds, bool allowProtectedReformat = false) {
        if (previous is null) return;
        var allowed = allowedIds.Concat(previous.TranscriptIds).Concat(previous.Citations.SelectMany(x => x.SourceIds)).Distinct().ToList();
        var citations = CloneCitations(previous.Citations);
        citations.AddRange(update.Citations.Where(x => citations.All(c => c.Id != x.Id)));
        string Canonical(string text) => Compact(previous.SessionId, Expand(text, citations), allowed, citations).Replace("\r\n", "\n");
        foreach (var old in Read(previous, allowed)) {
            var current = update.Sections.SingleOrDefault(x => x.Id == old.Id);
            if (current is null)
            {
                // A topic the organizer emptied may go once every block it held lives on elsewhere; no text goes with it.
                var remaining = update.Sections.SelectMany(x => x.Blocks).Select(x => x.Id).ToHashSet();
                if (!old.UserEdited && (old.Blocks.Count > 0 || old.Points.Count == 0) && old.Blocks.All(x => remaining.Contains(x.Id))) continue;
                throw new InvalidOperationException("AI update removed a saved section; result was not applied");
            }
            var body = Canonical(current.Markdown);
            if (old.Blocks.Count > 0 && update.Sections.Any(x => x.Blocks.Count > 0))
            {
                var blocks = update.Sections.SelectMany(x => x.Blocks).ToList();
                foreach (var prior in old.Blocks)
                {
                    var retained = blocks.SingleOrDefault(x => x.Id == prior.Id);
                    if (update.EditedBlockIds?.Contains(prior.Id) == true)
                    {
                        // An approved edit may change or remove the block, never the evidence it cites.
                        var kept = (retained is null ? blocks : [retained]).SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds);
                        if (prior.Anchors.SelectMany(x => x.SourceIds).Except(kept).Any())
                            throw new InvalidOperationException("An approved edit lost a block's evidence");
                        continue;
                    }
                    if (retained is null || Canonical(retained.Body) != Canonical(prior.Body) || prior.Anchors.SelectMany(x => x.SourceIds).Except(retained.Anchors.SelectMany(x => x.SourceIds)).Any())
                        throw new InvalidOperationException("Structural operation lost a canonical block or its evidence");
                    if ((old.UserEdited || prior.UserEdited) && !allowProtectedReformat && !current.Blocks.Any(x => x.Id == prior.Id))
                        throw new InvalidOperationException("Protected block moved without an explicit scope action");
                }
                continue;
            }
            if (old.UserEdited && !allowProtectedReformat && Canonical(old.Markdown) != body && old.Blocks.Count == 0 || old.Points.Any(x => !body.Contains(Canonical(x.Text), StringComparison.Ordinal)) ||
                old.Blocks.Any(x => !body.Contains(Canonical(x.Body), StringComparison.Ordinal)))
                throw new InvalidOperationException("AI update lost saved point text or changed a protected section; result was not applied");
        }
    }
    public static SectionUpdate Apply(string sessionId, Note? latest, List<NoteSection> baseSections, NotePatch patch,
        IEnumerable<string> editableIds, IEnumerable<string> inputIds, IEnumerable<string> allowedIds, string inputHash, bool allowUserEdited = false) {
        var editable = editableIds.ToHashSet(); var input = inputIds.ToHashSet(); var allowed = allowedIds.ToHashSet();
        var citations = CloneCitations(latest?.Citations ?? []); var result = baseSections.ToList();
        var touched = new HashSet<string>(); var coverage = new List<SourceDisposition>();
        if (patch.Sections.Count > 4) throw new InvalidOperationException("Section response exceeds the update limit");
        foreach (var update in patch.Sections) {
            var existing = result.FirstOrDefault(x => x.Id == update.Id);
            if (existing is not null && !touched.Add(update.Id)) throw new InvalidOperationException("Duplicate section update");
            if (existing is not null && (!editable.Contains(existing.Id) || existing.UserEdited && !allowUserEdited || existing.Version != update.BaseVersion))
                throw new InvalidOperationException("Section is protected or its base version changed");
            if (existing is null && update.Id != "new") throw new InvalidOperationException("Unknown section identity");
            if (update.Markdown.Trim().Length is < 1 or > 32000 || update.Title.Trim().Length is < 1 or > 200)
                throw new InvalidOperationException("Invalid section content size");
            if (existing is not null && existing.Points.Any(point => !update.Points.Any(x => x.Retains.Contains(point.Id))))
                throw new InvalidOperationException("Section response dropped an existing coverage point");
            var points = new List<NotePoint>();
            foreach (var point in update.Points) {
                if (point.Text.Trim().Length is < 1 or > 8000 || !update.Markdown.Contains(point.Text, StringComparison.Ordinal))
                    throw new InvalidOperationException("Coverage point must appear verbatim in the section");
                if (point.SourceIds.Any(x => !allowed.Contains(x))) throw new InvalidOperationException("Unknown coverage source");
                if (point.Retains.Any(x => existing is null || !existing.Points.Any(p => p.Id == x)))
                    throw new InvalidOperationException("Unknown retained point");
                var priorSources = existing?.Points.Where(x => point.Retains.Contains(x.Id)).SelectMany(x => x.SourceIds) ?? [];
                if (priorSources.Except(point.SourceIds).Any()) throw new InvalidOperationException("Compression lost the earlier point's provenance");
                var cited = Sources(Expand(point.Text, citations), allowed);
                if (point.SourceIds.Except(cited).Any()) throw new InvalidOperationException("Coverage has no citation in the section");
                var id = point.Retains.Count == 1 ? point.Retains[0] : "point_" + ContentHash.Of(sessionId + inputHash + point.Text)[..20];
                points.Add(new NotePoint { Id = id, Text = point.Text, SourceIds = point.SourceIds.Distinct().ToList() });
            }
            if (points.Count == 0) throw new InvalidOperationException("Section has no observable coverage points");
            // A model can claim `retains` while omitting the formula/condition in its prose.
            // Organizing also needs this protection: retains + citations alone cannot prove
            // that a formula, qualification or example survived a model's rewrite.
            if (existing is not null) {
                var compactOutput = Compact(sessionId, Expand(update.Markdown, citations), allowed, citations).Replace("\r\n", "\n");
                var missing = existing.Points.Where(p => !compactOutput.Contains(
                    Compact(sessionId, Expand(p.Text, citations), allowed, citations).Replace("\r\n", "\n"), StringComparison.Ordinal)).ToList();
                if (missing.Count > 0) {
                    throw new InvalidOperationException("Body rewrite requires a retained-block preview; previous prose remains in its original position");
                }
            }
            foreach (var point in points) point.Text = Compact(sessionId, Expand(point.Text, citations), allowed, citations);
            var section = new NoteSection { Id = existing?.Id ?? "section_" + ContentHash.Of(sessionId + inputHash + update.Title)[..20],
                Version = (existing?.Version ?? 0) + 1, Title = update.Title,
                ParentId = existing?.ParentId ?? update.ParentId, Order = existing?.Order ?? result.Count,
                Markdown = Compact(sessionId, Expand(update.Markdown, citations), allowed, citations), Points = points, UserEdited = existing?.UserEdited ?? false };
            if (existing is null && !touched.Add(section.Id)) throw new InvalidOperationException("Duplicate new section title");
            if (existing is null) result.Add(section); else result[result.IndexOf(existing)] = section;
        }
        var allPoints = result.SelectMany(x => x.Points).ToList();
        foreach (var id in input) {
            var points = allPoints.Where(x => x.SourceIds.Contains(id)).Select(x => x.Id).ToList();
            var disposition = patch.Deferred.SingleOrDefault(x => x.SourceId == id);
            var deferred = disposition is not null && !string.IsNullOrWhiteSpace(disposition.Reason);
            coverage.Add(new SourceDisposition { SourceId = id, Status = points.Count > 0 ? "covered" : deferred ? "deferred" : "pending",
                Reason = points.Count > 0 ? "Source-backed points saved; semantic quality still requires review" : deferred ? disposition!.Reason :
                    "The model omitted this source without a written point or deferral. It remains pending for another decision.",
                PointIds = points });
        }
        if (patch.Sections.Count == 0 && patch.Operations.Count == 0 && coverage.Any(x => x.Status == "pending"))
            throw new InvalidOperationException("The model returned no section and left input sources unaddressed");
        if (patch.Deferred.Any(x => !input.Contains(x.SourceId))) throw new InvalidOperationException("Unknown deferred source");
        return new SectionUpdate(result, UsedCitations(result, citations), coverage);
    }

    public static RecoveryPreview Recover(Note historical, Note? latest, IEnumerable<string> allowedIds) {
        var allowed = allowedIds.ToHashSet(); var current = Read(latest, allowed); var history = Read(historical, allowed);
        var citations = CloneCitations(latest?.Citations ?? []);
        var suppressed = (latest?.SuppressedSourceIds ?? []).ToHashSet();
        var present = current.SelectMany(x => x.Points).SelectMany(x => x.SourceIds).ToHashSet();
        var candidates = history.Where(x => !(latest?.DeletedSectionIds.Contains(x.Id) ?? false)).Select(section => {
            var missing = section.Points.SelectMany(x => x.SourceIds).Except(present).Except(suppressed).Where(allowed.Contains).Distinct().ToList();
            var expanded = Expand(section.Markdown, historical.Citations);
            // A recovery never guesses whether a partly deleted paragraph was intentional.
            var blocked = section.Points.SelectMany(x => x.SourceIds).Any(suppressed.Contains);
            return new RecoveryCandidate("recovery_" + ContentHash.Of(historical.Id + section.Id)[..20], section.Title,
                Compact(historical.SessionId, expanded, allowed, citations), missing, blocked, section.UserEdited,
                section.Points);
        }).Where(x => x.MissingSourceIds.Count > 0).ToList();
        return new RecoveryPreview(latest?.Version ?? 0, historical.Version, current, citations, candidates);
    }
}
// A note reply. Like an author planning a document, the writer first places new content: Outline declares the
// new topics (key, title, parent) and every Block names the topic it goes in, an open existing topic or a new key.
// Operations edit, move, reorder or rename. Property order is the order the model writes them.
public sealed class NotePatch
{
    public List<OutlinePlacement> Outline { get; set; } = [];
    public List<PlacedBlock> Blocks { get; set; } = [];
    public List<NoteTreeOperation> Operations { get; set; } = [];
    public List<SourceDisposition> Deferred { get; set; } = [];
    public List<SectionPatch> Sections { get; set; } = [];
    public List<RewriteBlockChange> Rewrites { get; set; } = [];
}
// Key: new1…new8. Parent: "" for the top level, an existing topic ID, or another new key.
public sealed class OutlinePlacement { public string Key { get; set; } = ""; public string Title { get; set; } = ""; public string Parent { get; set; } = ""; }
// Topic: an open existing topic ID or a new key from Outline.
public sealed class PlacedBlock { public string Topic { get; set; } = ""; public string Role { get; set; } = "detail"; public string Body { get; set; } = ""; public List<string> SourceIds { get; set; } = []; }
public sealed class SectionPatch { public string Id { get; set; } = "new"; public int BaseVersion { get; set; } public string Title { get; set; } = "";
    public string? ParentId { get; set; }
    public string Markdown { get; set; } = ""; public List<PointPatch> Points { get; set; } = []; }
public sealed class PointPatch { public string Text { get; set; } = ""; public List<string> SourceIds { get; set; } = []; public List<string> Retains { get; set; } = []; }
// EditedBlockIds: blocks an approved edit (replace-text/delete-block judged by Jev) may change or remove in this update.
public sealed record SectionUpdate(List<NoteSection> Sections, List<NoteCitation> Citations, List<SourceDisposition> Coverage, string? Presentation = null,
    IReadOnlyCollection<string>? EditedBlockIds = null);
public sealed record RecoveryCandidate(string Id, string Title, string Markdown, List<string> MissingSourceIds, bool Blocked, bool UserEdited, List<NotePoint> Points);
public sealed record RecoveryPreview(int BasedOnVersion, int HistoricalVersion, List<NoteSection> CurrentSections, List<NoteCitation> Citations, List<RecoveryCandidate> Candidates);
