#:property NuGetAudit=false
#:property PublishAot=false

// This offline file has no third-party packages. PublishAot=false avoids an SDK package restore,
// and NuGetAudit=false prevents a remote audit lookup, so the default study is genuinely offline.

using System.Text;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;

var selfTest = args.Contains("--self-test", StringComparer.OrdinalIgnoreCase);
var chunks = new[]
{
    new TranscriptChunk(1, "00:00", "A cache stores reusable results. A cache hit avoids repeating expensive work."),
    new TranscriptChunk(2, "00:40", "Cache invalidation matters because stale values can make an answer wrong."),
    new TranscriptChunk(3, "01:20", "The lecturer compares time-to-live and explicit invalidation strategies.")
};

var note = RollingNote.Empty;
foreach (var chunk in chunks)
{
    note = DemoNoteEditor.Apply(note, chunk);
    Console.WriteLine($"revision={note.Revision}, processedThrough={note.ProcessedThrough}");
}

// The same ASR chunk can be delivered twice after retry. It must not duplicate the note.
var afterDuplicate = DemoNoteEditor.Apply(note, chunks[2]);

if (selfTest)
{
    Expect(afterDuplicate == note, "a repeated chunk is idempotent");
    ExpectThrows(
        () => DemoNoteEditor.Apply(RollingNote.Empty, chunks[1]),
        "an out-of-order chunk cannot skip the contiguous watermark");
    Expect(note.ProcessedThrough == 3, "watermark advances to chunk 3");
    Expect(note.Markdown.Contains("Cache invalidation", StringComparison.Ordinal), "important content reaches the note");
    Console.WriteLine("SELF-TEST PASS");
    return;
}

Console.WriteLine();
Console.WriteLine(note.Markdown);
Console.WriteLine("Duplicate delivery changed state: " + (afterDuplicate != note));

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("Self-test failed: " + message);
    }
}

static void ExpectThrows(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException("Self-test failed: " + message);
}

internal sealed record TranscriptChunk(long Id, string StartedAt, string Text);

internal sealed record RollingNote(long ProcessedThrough, int Revision, string Markdown)
{
    public static RollingNote Empty { get; } = new(0, 0, "# Lecture note\n");
}

internal static class DemoNoteEditor
{
    public static RollingNote Apply(RollingNote current, TranscriptChunk chunk)
    {
        if (chunk.Id <= current.ProcessedThrough)
        {
            return current;
        }
        if (chunk.Id != current.ProcessedThrough + 1)
        {
            throw new InvalidOperationException(
                $"Chunk gap: expected {current.ProcessedThrough + 1}, received {chunk.Id}. Buffer or retry it; do not advance the watermark.");
        }

        var cleanText = Regex.Replace(chunk.Text.Trim(), @"\s+", " ");
        var addition = $"\n## {chunk.StartedAt}\n- {cleanText}\n";

        return new RollingNote(
            ProcessedThrough: chunk.Id,
            Revision: current.Revision + 1,
            Markdown: current.Markdown + addition);
    }
}
