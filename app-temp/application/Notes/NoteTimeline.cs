using System.Text.RegularExpressions;
using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// A transcript line placed on the session's clock: milliseconds since its first speech.
public sealed record NoteLine(NoteTranscript Source, long At)
{
    public string Id => Source.Id;
    public string Text => Source.SourceText;
}

// Five minutes of the session's speech, the unit a segment is written from. Hash identifies its text, so a corrected or
// re-recognized transcript gives the window a new hash and its segment is due again.
public sealed record NoteWindow(int Index, List<NoteLine> Lines, string Hash)
{
    public long StartMs => Lines[0].At;
    public long EndMs => Lines.Max(x => x.At + Math.Max(0, x.Source.EndMs - x.Source.StartMs));
}

// What is due for a session: segments to write (oldest first) and, while recording, the minute to jot.
public sealed record NotePlan(List<NoteWindow> Segments, int? JotMinute);

public static class NoteTimeline
{
    public const long WindowMs = 300_000;
    public const long MinuteMs = 60_000;

    public static List<NoteLine> Lines(NoteSession session)
    {
        var spoken = session.Transcripts.Where(x => !string.IsNullOrWhiteSpace(x.SourceText)).ToList();
        var origin = spoken.Where(x => x.RecordedAt.HasValue).Select(x => x.RecordedAt!.Value).DefaultIfEmpty().Min();
        long At(NoteTranscript x) => x.RecordedAt is { } at && origin != default ? (long)(at - origin).TotalMilliseconds : x.StartMs;
        return spoken.Select(x => new NoteLine(x, At(x))).OrderBy(x => x.At).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    public static List<NoteWindow> Windows(NoteSession session) => Lines(session).GroupBy(x => (int)(x.At / WindowMs))
        .Select(group => new NoteWindow(group.Key, group.ToList(),
            ContentHash.Of(session.NoteLanguage + "\n" + string.Join("\n", group.Select(x => x.Id + "\t" + x.Text)))))
        .ToList();

    // A window is closed once later speech exists, or when every window counts as closed (after Stop, or on request).
    // A closed window is due when it has no segment, or its segment was written from different text.
    public static NotePlan Plan(NoteSession session, bool closeAll)
    {
        var windows = Windows(session);
        if (windows.Count == 0) return new([], null);
        var segments = session.CurrentNote?.Segments ?? [];
        var latest = windows[^1].Lines[^1].At;
        var due = windows.Where(w => closeAll || latest >= (w.Index + 1) * WindowMs)
            .Where(w => segments.FirstOrDefault(s => s.Index == w.Index)?.InputHash != w.Hash).ToList();
        if (closeAll) return new(due, null);
        // Only the last finished minute is jotted; older unjotted minutes are left to their segment.
        var minute = (int)(latest / MinuteMs) - 1;
        var jotted = session.Sections.SelectMany(x => x.Blocks).Any(b => b.Id == NoteLayout.LiveBlockId(minute));
        var written = segments.Any(s => s.Index == minute * MinuteMs / WindowMs);
        var spoken = windows.SelectMany(w => w.Lines).Any(x => x.At / MinuteMs == minute);
        return new(due, minute >= 0 && spoken && !jotted && !written ? minute : null);
    }

    public static string Clock(long ms)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}

// Short aliases (T1, T2, …) for the source IDs in one model call. The model cites aliases; Decode turns them back into
// source IDs and drops any it invented, and Encode does the reverse for notes sent as input.
public sealed class NoteAliases
{
    readonly Dictionary<string, string> byAlias = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> bySource = new(StringComparer.Ordinal);

    public NoteAliases(IEnumerable<string> sourceIds)
    {
        foreach (var id in sourceIds.Distinct(StringComparer.Ordinal))
        {
            var alias = "T" + (byAlias.Count + 1);
            byAlias[alias] = id;
            bySource[id] = alias;
        }
    }

    public string Alias(string sourceId) => bySource[sourceId];
    public IEnumerable<string> Sources(IEnumerable<string> aliases) => aliases.SelectMany(Expand).Where(byAlias.ContainsKey).Select(x => byAlias[x]).Distinct();
    public List<object[]> Rows(IEnumerable<NoteLine> lines) =>
        lines.Select(x => new object[] { bySource[x.Id], NoteTimeline.Clock(x.At), x.Source.SourceId, x.Text }).ToList();

    public string Decode(string text) => Regex.Replace(CitationSyntax.Rewrite(text, body =>
    {
        var tokens = body.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || !tokens.All(x => Regex.IsMatch(x, @"^T\d+(?:\s*[-–]\s*T?\d+)?$"))) return body;
        return string.Join(", ", Sources(tokens));
    }), @"[ \t]*\[\]", "");

    public string Encode(string text) => CitationSyntax.Rewrite(text, body =>
    {
        var tokens = body.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(bySource.ContainsKey) ? string.Join(", ", tokens.Select(x => bySource[x])) : body;
    });

    // "T3-T6" is read as T3, T4, T5, T6 even though the prompt asks for single aliases.
    static IEnumerable<string> Expand(string token)
    {
        var range = Regex.Match(token, @"^T(\d+)\s*[-–]\s*T?(\d+)$");
        if (!range.Success) return [token.Trim()];
        var (from, to) = (int.Parse(range.Groups[1].Value), int.Parse(range.Groups[2].Value));
        return to < from || to - from > 60 ? [] : Enumerable.Range(from, to - from + 1).Select(x => "T" + x);
    }
}
