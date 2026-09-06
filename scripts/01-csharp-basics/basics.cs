#:property TargetFramework=net10.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true
#:property PublishAot=false

Console.OutputEncoding = System.Text.Encoding.UTF8;

string courseTitle = "Introduction to Distributed Systems";
int lectureMinutes = 180;
double chunkSeconds = 15.0;
bool isRecording = true;
var chunkCount = (int)Math.Ceiling(lectureMinutes * 60 / chunkSeconds);

Console.WriteLine(
    $"[types] {courseTitle} | {lectureMinutes} min | " +
    $"chunk={chunkSeconds:F0}s | recording={isRecording} | expected={chunkCount}");

var sessionId = Guid.Parse("58d07f8e-76f7-44e8-95e9-4e4f493157d8");
var draft = new LectureChunk(sessionId, 0, "  CAP theorem intro  ", 14.8);
var cleaned = draft with { Text = draft.Text.Trim() };

Console.WriteLine($"[record] {cleaned}");
Console.WriteLine($"[record] value-equality={cleaned == new LectureChunk(sessionId, 0, "CAP theorem intro", 14.8)}");

List<LectureChunk> chunks =
[
    cleaned,
    new(sessionId, 2, "Eventual consistency examples", 15.0),
    new(sessionId, 1, "Network partitions", 15.0),
    new(sessionId, 3, "   ", 4.2)
];

var noteLines = chunks
    .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text))
    .OrderBy(chunk => chunk.Sequence)
    .Select(chunk => $"{chunk.Sequence + 1}. {chunk.Text}")
    .ToList();

Console.WriteLine($"[list+linq] input={chunks.Count}, useful={noteLines.Count}");
foreach (var line in noteLines)
{
    Console.WriteLine($"  {line}");
}

string? lecturerName = null;
int? currentSlide = 12;
string lecturerLabel = lecturerName?.Trim() ?? "（未提供講師名稱）";
Console.WriteLine($"[nullable] lecturer={lecturerLabel}, slide={currentSlide ?? 0}");

string normalized = await NormalizeTranscriptAsync(cleaned.Text);
Console.WriteLine($"[async] normalized={normalized}");

try
{
    _ = ParsePositiveInterval("-5");
}
catch (ArgumentOutOfRangeException exception)
{
    Console.WriteLine($"[error] handled={exception.GetType().Name}, parameter={exception.ParamName}");
}

Console.WriteLine("PASS: C# basics spike completed.");

static async Task<string> NormalizeTranscriptAsync(string text)
{
    await Task.Delay(25);
    return text.ToUpperInvariant();
}

static int ParsePositiveInterval(string input)
{
    if (!int.TryParse(input, out int value))
    {
        throw new FormatException("Interval must be a whole number.");
    }

    return value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(input), "Interval must be positive.");
}

public sealed record LectureChunk(
    Guid SessionId,
    long Sequence,
    string Text,
    double DurationSeconds);
