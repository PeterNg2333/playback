using Playback.Api.Db;
using System.Text.RegularExpressions;

namespace Playback.Api.Sources;

public sealed record TranscriptSourceGroup(string Id, string SourceId, List<string> TranscriptIds,
    long StartMs, long EndMs, DateTime? RecordedAt);

// Short IDs are prompt-local aliases. Durable chunk IDs and saved citations remain unchanged.
public sealed class SourceReferences
{
    readonly Dictionary<string, string> shortIds = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> sourceIds = new(StringComparer.Ordinal);
    readonly Dictionary<string, Transcript> transcripts;
    public List<TranscriptSourceGroup> Groups { get; }
    static readonly Regex Brackets = new(@"(?<code>```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\r\n]*`)|\[(?<body>[^\[\]\r\n]{1,16000})\](?!\()", RegexOptions.Compiled);
    public SourceReferences(SessionView session)
    {
        transcripts = session.Transcripts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        DateTime At(Transcript source) => source.RecordedAt ?? session.CreatedAt.AddMilliseconds(source.StartMs);
        var groups = new List<List<Transcript>>();
        foreach (var source in session.Transcripts.Where(x => !string.IsNullOrWhiteSpace(x.SourceText)).GroupBy(x => x.SourceId)) {
            List<Transcript>? group = null;
            foreach (var entry in source.OrderBy(At).ThenBy(x => x.Id, StringComparer.Ordinal)) {
                var previous = group?.Last();
                var previousEnd = previous is null ? DateTime.MinValue : At(previous).AddMilliseconds(previous.EndMs - previous.StartMs);
                if (group is null || group.Count >= 16 || At(entry) < previousEnd.AddMilliseconds(-1000) ||
                    At(entry) > previousEnd.AddMilliseconds(1500) ||
                    At(entry).AddMilliseconds(entry.EndMs - entry.StartMs) - At(group[0]) > TimeSpan.FromMinutes(1)) {
                    group = []; groups.Add(group);
                }
                group.Add(entry);
            }
        }
        Groups = groups.OrderBy(x => At(x[0])).ThenBy(x => x[0].Id, StringComparer.Ordinal)
            .Select((group, index) => new TranscriptSourceGroup((index + 1).ToString("D2"), group[0].SourceId,
                group.Select(x => x.Id).ToList(), group[0].StartMs, group[^1].EndMs, At(group[0]))).ToList();
        foreach (var group in Groups) {
            foreach (var id in group.TranscriptIds) shortIds[id] = group.Id;
            sourceIds[group.Id] = string.Join(", ", group.TranscriptIds);
        }
        var index = 0;
        foreach (var source in session.Materials.OrderBy(x => x.Id, StringComparer.Ordinal)) Add(source.Id, "M" + (++index).ToString("D2"));
    }
    void Add(string id, string alias) { shortIds[id] = alias; sourceIds[alias] = id; }
    public string ShortId(string id) => shortIds[id];
    public List<TranscriptSourceGroup> SelectedGroups(IEnumerable<Transcript> selected) {
        var ids = selected.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return Groups.Where(group => group.TranscriptIds.Any(ids.Contains)).ToList();
    }
    public List<Transcript> Expand(IEnumerable<Transcript> selected) => SelectedGroups(selected)
        .SelectMany(group => group.TranscriptIds.Select(id => transcripts[id])).ToList();
    public string GroupText(TranscriptSourceGroup group) => string.Join(" ", group.TranscriptIds.Select(id => transcripts[id].SourceText));
    public string Prompt(IEnumerable<Transcript> selected, int maxChars = 3000) => string.Join("\n", SelectedGroups(selected).Select(group => {
        var text = GroupText(group);
        return $"Source [{group.Id}] ({group.StartMs}-{group.EndMs} ms, {group.SourceId}): {text[..Math.Min(text.Length, maxChars)]}";
    }));
    public string Encode(string text) => Translate(text, shortIds, false);
    public string Decode(string text) => Translate(text, sourceIds, true);
    public static IEnumerable<string> CitationBodies(string text) => Brackets.Matches(text)
        .Where(match => !match.Groups["code"].Success).Select(match => match.Groups["body"].Value);
    string Translate(string text, Dictionary<string, string> map, bool validate) => Brackets.Replace(text, match => {
        if (match.Groups["code"].Success) return match.Value;
        var body = match.Groups["body"].Value;
        if (body.StartsWith("ref:", StringComparison.OrdinalIgnoreCase)) return match.Value;
        var ids = body.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length > 0 && ids.All(map.ContainsKey))
            return "[" + string.Join(", ", ids.SelectMany(id => map[id].Split(", ")).Distinct(StringComparer.Ordinal)) + "]";
        var converted = Regex.Replace(body, @"[A-Za-z0-9_-]+", token => {
            if (map.TryGetValue(token.Value, out var value)) return value;
            if (validate && !shortIds.ContainsKey(token.Value) && Regex.IsMatch(token.Value, @"^(?:\d+|M\d+)$"))
                throw new InvalidOperationException("AI answer contains an unknown short source reference");
            return token.Value;
        });
        return "[" + converted + "]";
    });
}
