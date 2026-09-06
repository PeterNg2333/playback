// List<T> + LINQ 練習：由一疊 chunk 變成有次序、乾淨的筆記行
var sessionId = Guid.NewGuid();

List<LectureChunk> chunks =
[
    new(sessionId, 2, "Eventual consistency examples", 15.0),
    new(sessionId, 0, "CAP theorem intro", 14.8),
    new(sessionId, 3, "   ", 4.2),
    new(sessionId, 1, "Network partitions", 15.0),
];

var noteLines = chunks
    .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text))
    .OrderBy(chunk => chunk.Sequence)
    .Select(chunk => $"{chunk.Sequence + 1}. {chunk.Text}")
    .ToList();

Console.WriteLine($"input={chunks.Count}, useful={noteLines.Count}");
foreach (var line in noteLines)
{
    Console.WriteLine($"  {line}");
}

public sealed record LectureChunk(
    Guid SessionId,
    long Sequence,
    string Text,
    double DurationSeconds);
