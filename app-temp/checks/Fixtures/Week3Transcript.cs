using Playback.Api;
using System.Text.RegularExpressions;
using Playback.Api.Db;

// The repository's Week 3 lecture transcript as text-import sources. Each timestamp line ends the passage above
// it; neighbouring passages are paired into one source. The text is exact, so this is provider-input evidence,
// not new audio recognition.
static class Week3Transcript
{
    public const string RelativePath = "app-temp/data/test-audio/sampleAudio/transcript.txt";

    public static List<(long Start, long End, string Text)> Passages(string text, long throughMs)
    {
        var passages = new List<(long Start, long End, string Text)>();
        long start = 0;
        var lines = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (Regex.IsMatch(line.Trim(), @"^\d{1,3}:\d{2}(?::\d{2})?$"))
            {
                var end = line.Trim().Split(':').Aggregate(0L, (value, part) => value * 60 + long.Parse(part)) * 1000;
                if (lines.Count > 0 && start < throughMs && end > start) passages.Add((start, Math.Min(end, throughMs), string.Join(' ', lines).Trim()));
                start = end;
                lines.Clear();
            }
            else if (line.Trim().Length > 0) lines.Add(line.Trim());
        }
        return passages;
    }

    public static List<(long Start, long End, string Text)> Pairs(List<(long Start, long End, string Text)> passages) =>
        passages.Chunk(2).Select(parts => (Start: parts[0].Start, End: parts[^1].End, Text: string.Join(" ", parts.Select(x => x.Text)))).ToList();

    public static List<Transcript> Sources(string sessionId, string text, long throughMs)
    {
        var sourceHash = ContentHash.Of(text);
        var epoch = DateTime.UtcNow.Date;
        return Pairs(Passages(text, throughMs)).Select(p => new Transcript
        {
            Id = "week3_" + ContentHash.Of($"{sourceHash}:{p.Start}:{p.Text}")[..24], SessionId = sessionId, SourceId = "week3-text",
            StartMs = p.Start, EndMs = p.End, RecordedAt = epoch.AddMilliseconds(p.Start), Original = p.Text, NoteStatus = "pending"
        }).ToList();
    }
}
