// Record 練習：LectureChunk 是一筆不可變的講課片段資料
var sessionId = Guid.NewGuid();
var draft = new LectureChunk(sessionId, 0, "  CAP theorem intro  ", 14.8);
var cleaned = draft with { Text = draft.Text.Trim() };

Console.WriteLine($"draft   = {draft}");
Console.WriteLine($"cleaned = {cleaned}");

var same = new LectureChunk(sessionId, 0, "CAP theorem intro", 14.8);
Console.WriteLine($"cleaned == same content -> {cleaned == same}");

public sealed record LectureChunk(
    Guid SessionId,
    long Sequence,
    string Text,
    double DurationSeconds);
