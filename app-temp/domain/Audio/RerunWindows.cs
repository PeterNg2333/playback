using Playback.Api.Db;

namespace Playback.Domain.Audio;

// Plans rerun windows: consecutive saved chunks of one capture stream re-recognized together, so the provider hears
// whole sentences instead of 8-second fragments. A chunk sealed early (under 7.5 s) ended at a speech pause, and a
// quiet chunk is a pause; after the target length the window closes at the next pause, or 20 s later without one, and never passes the cap.
// An existing window segment is never split, so every superseded row is replaced completely.
public static class RerunWindows
{
    public const int MinSeconds = 20;
    public const int MaxSeconds = 90;
    public const long PauseBelowMs = 7_500;
    // Without a pause, a window closes this long after the target instead of running to the cap.
    public const int NoPauseSlackSeconds = 20;
    const long ContiguousMs = 50;

    public static void Validate(int targetSeconds)
    {
        if (targetSeconds is < MinSeconds or > MaxSeconds - 10) throw new InvalidOperationException($"Window length must be {MinSeconds}–{MaxSeconds - 10} seconds");
    }

    static bool Quiet(ChunkRecord chunk) => QuietAudio.IsQuiet(chunk.Status);
    static bool Eligible(ChunkRecord chunk) => Quiet(chunk) || chunk.Status is ChunkStatus.Transcribed or ChunkStatus.AsrEmpty or ChunkStatus.AsrManual or ChunkStatus.AsrError;

    public static List<RerunWindow> Plan(string jobId, IEnumerable<ChunkRecord> chunks, IEnumerable<Transcript> transcripts, int targetSeconds)
    {
        Validate(targetSeconds);
        var active = transcripts.Where(x => x.Active).ToList();
        var unit = new Dictionary<string, Transcript>(StringComparer.Ordinal);
        foreach (var row in active) foreach (var chunk in row.CoveredChunkIds) unit[chunk] = row;
        var windows = new List<RerunWindow>();
        foreach (var stream in chunks.GroupBy(x => (x.SessionId, x.SourceId, x.StreamId ?? "")))
        {
            var run = new List<ChunkRecord>();
            string? Unit(ChunkRecord chunk) => unit.TryGetValue(chunk.Id, out var row) ? row.Id : Quiet(chunk) ? null : chunk.Id;
            void Close()
            {
                var first = run.FindIndex(x => !Quiet(x)); var last = run.FindLastIndex(x => !Quiet(x));
                if (first >= 0) windows.Add(Window(jobId, run[first..(last + 1)], unit));
                run.Clear();
            }
            var ordered = stream.OrderBy(x => x.StartMs).ThenBy(x => x.Sequence).ToList();
            var lastOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < ordered.Count; i++) if (Unit(ordered[i]) is { } id) lastOf[id] = i;
            var openUntil = -1;
            for (var i = 0; i < ordered.Count; i++)
            {
                var chunk = ordered[i];
                if (!Eligible(chunk) || chunk.EndMs <= chunk.StartMs) { Close(); openUntil = -1; continue; }
                if (run.Count == 0 && Quiet(chunk)) continue; // A window starts with speech, so its length counts from speech.
                var previous = run.LastOrDefault();
                if (previous is not null && i > openUntil && (Math.Abs(chunk.StartMs - previous.EndMs) > ContiguousMs ||
                    chunk.EndMs - run[0].StartMs > MaxSeconds * 1000L)) Close();
                run.Add(chunk);
                if (Unit(chunk) is { } current) openUntil = Math.Max(openUntil, lastOf[current]);
                var next = i + 1 < ordered.Count ? ordered[i + 1] : null;
                var pause = Quiet(chunk) || chunk.EndMs - chunk.StartMs < PauseBelowMs || next is null || Quiet(next);
                var length = chunk.EndMs - run[0].StartMs;
                if (i >= openUntil && length >= targetSeconds * 1000L && (pause || length >= (targetSeconds + NoPauseSlackSeconds) * 1000L)) Close();
            }
            Close();
        }
        return windows.OrderBy(x => x.RecordedAt).ThenBy(x => x.StartMs).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToList();
    }

    static RerunWindow Window(string jobId, List<ChunkRecord> chunks, Dictionary<string, Transcript> unit)
    {
        var speech = chunks.Where(x => !Quiet(x)).ToList();
        var identity = ContentHash.Of("rerun-window-v1:" + jobId + ":" + string.Join("|", chunks.Select(x => x.Id)));
        return new RerunWindow
        {
            Id = $"{chunks[0].SessionId}-window-{identity[..24]}",
            AudioId = "rerun_" + identity,
            SourceId = chunks[0].SourceId,
            StreamId = chunks[0].StreamId ?? "",
            StartMs = speech[0].StartMs,
            EndMs = speech[^1].EndMs,
            RecordedAt = speech[0].RecordedAt,
            ChunkIds = chunks.Select(x => x.Id).ToList(),
            SpeechChunkIds = speech.Select(x => x.Id).ToList(),
            ReplacesIds = speech.Select(x => unit.TryGetValue(x.Id, out var row) ? row.Id : null).OfType<string>().Distinct(StringComparer.Ordinal).ToList()
        };
    }
}
