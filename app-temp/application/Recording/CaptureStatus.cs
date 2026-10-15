namespace Playback.Api.Audio.Recording;
public sealed record ActiveCaptureSegment(string SourceId, long StartMs, long EndMs, DateTime RecordedAt, bool Streaming,
    string? InterimText = null, string? RawInterimText = null, long? LatencyMs = null, string? Model = null);

public sealed record CaptureStatus(string State, string? SessionId, Dictionary<string, long> Bytes, bool AutoAsr, bool NoSoundWarning, string? Error,
    Dictionary<string, int> Levels, long CapturedThroughMs, long LastFinalizedAtMs, ActiveCaptureSegment[] ActiveSegments,
    string? SourceMode = null, int ChunkMilliseconds = 8000, string? InterimError = null,
    long RecordingElapsedMs = 0, string? RecordingId = null);
