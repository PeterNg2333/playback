using Playback.Domain;

namespace Playback.Domain.Audio;

public sealed record AudioFormat(int Encoding, int SampleRateHz, int Channels, int BitsPerSample, int BlockAlign, string Descriptor);
public sealed record AudioPart(string Id, string SessionId, string SourceId, string StreamId, long Sequence,
    string OriginalHash, long StartMs, long EndMs, string Status, DateTime? RecordedAt, bool StreamClosed,
    AudioFormat? Format = null, long Frames = 0);
public sealed record CombinedAudioChild(string ChunkId, long Sequence, string OriginalHash, long FrameOffset, long FrameCount, long StartMs, long EndMs);
public sealed class CombinedAudioSegment
{
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string StreamId { get; set; } = "";
    public string PolicyRevision { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public AudioFormat Format { get; set; } = null!;
    public List<CombinedAudioChild> Children { get; set; } = [];
    public string DerivedHash { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string State { get; set; } = "building";
}
public static class QuietAudio
{
    public static List<CombinedAudioSegment> Playback(IEnumerable<CombinedAudioSegment> segments)
    {
        var used = new HashSet<string>();
        var result = new List<CombinedAudioSegment>();
        foreach (var segment in segments.OrderByDescending(x => x.Children.Count).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            if (segment.Children.Any(x => used.Contains(x.ChunkId))) continue;
            result.Add(segment);
            foreach (var child in segment.Children) used.Add(child.ChunkId);
        }
        return result.OrderBy(x => x.StartMs).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToList();
    }
    public static bool IsQuiet(string status) => status is "silent" or "vad-silence";
    public static List<CombinedAudioSegment> Plan(IEnumerable<AudioPart> parts, double maximumSeconds, string revision)
    {
        if (!double.IsFinite(maximumSeconds) || maximumSeconds is < 10 or > 300) throw new InvalidOperationException("Invalid quiet segment duration");
        var result = new List<CombinedAudioSegment>();
        foreach (var stream in parts.GroupBy(x => (x.SessionId, x.SourceId)))
        {
            var run = new List<AudioPart>();
            void Close()
            {
                if (run.Count == 0) return;
                var first = run[0]; long offset = 0;
                var segment = new CombinedAudioSegment { Id = "quiet_" + ContentHash.Of("quiet-v1:" + revision + ":" + first.StreamId + ":" + string.Join("|", run.Select(x => x.Id))),
                    SessionId = first.SessionId, SourceId = first.SourceId, StreamId = first.StreamId, PolicyRevision = revision,
                    StartMs = first.StartMs, EndMs = run[^1].EndMs, RecordedAt = first.RecordedAt, Format = first.Format! };
                foreach (var part in run) { segment.Children.Add(new(part.Id, part.Sequence, part.OriginalHash, offset, part.Frames, part.StartMs, part.EndMs)); offset += part.Frames; }
                result.Add(segment); run.Clear();
            }
            foreach (var part in stream.OrderBy(x => x.StartMs).ThenBy(x => x.Sequence))
            {
                if (!IsQuiet(part.Status) || part.Format is null || part.Frames <= 0) { Close(); continue; }
                var previous = run.LastOrDefault();
                if (previous is not null && (previous.StreamId != part.StreamId || previous.Format != part.Format || previous.EndMs != part.StartMs ||
                    part.EndMs - run[0].StartMs > maximumSeconds * 1000 || (run.Sum(x => x.Frames) + part.Frames) > maximumSeconds * part.Format.SampleRateHz || run.Count >= 128 ||
                    previous.RecordedAt is { } at && part.RecordedAt is { } next && Math.Abs((next - at).TotalMilliseconds - (previous.EndMs - previous.StartMs)) > 50)) Close();
                if (part.EndMs <= part.StartMs || part.EndMs - part.StartMs > maximumSeconds * 1000 || part.Frames > maximumSeconds * part.Format.SampleRateHz) { Close(); continue; }
                run.Add(part);
                if (part.EndMs - run[0].StartMs >= maximumSeconds * 1000) Close();
            }
            if (run.LastOrDefault()?.StreamClosed == true) Close();
        }
        return result;
    }
}

public sealed record SourceSpeech(string SourceId, long LastSpeechMs, long ObservedThroughMs);
public static class SpeechBoundaryPolicy
{
    public static bool Ready(IReadOnlyList<SourceSpeech> sources, long nowMs, double quietSeconds, long handledSpeechMs) =>
        sources.Count > 0 && sources.Any(x => x.LastSpeechMs > handledSpeechMs) &&
        sources.All(x => x.ObservedThroughMs >= nowMs && nowMs - x.LastSpeechMs >= quietSeconds * 1000);
}
