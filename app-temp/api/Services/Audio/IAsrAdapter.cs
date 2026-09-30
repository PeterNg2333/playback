namespace Playback.Api.Services.Audio;

public sealed record AsrRequest(string Path, string SessionId, string SourceId, long Sequence, string Hash, string Language = "auto", string? Model = null);
public sealed record AsrModel(string Provider, string Model, string Transport, bool SupportsLanguageHint = false);
public sealed record AsrResult(
    string Text,
    double? DurationSeconds,
    double? InferenceSeconds,
    string? Language,
    double? RealTimeFactor,
    AsrModel? Model = null,
    string? UsageJson = null);

// Saved chunks are the recovery boundary, regardless of the provider's transport.
public interface IAsrAdapter
{
    AsrModel Model { get; }
    Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct);
}

// A realtime provider additionally owns one ordered PCM stream per audio source.
// Partial hypotheses must not be saved as completed chunk transcripts.
public interface IStreamingAsrAdapter : IAsrAdapter
{
    Task<IAsrStream> OpenStream(AsrStreamRequest request, CancellationToken ct);
}

public sealed record AsrStreamRequest(string SessionId, string SourceId, string Language, string? Model = null);
public sealed record AsrStreamUpdate(long StartMs, long EndMs, string Text, bool IsFinal);

public interface IAsrStream : IAsyncDisposable
{
    // Updates use milliseconds relative to the first PCM sample sent. Same StartMs revises a hypothesis.
    // 16 kHz, mono, little-endian PCM16; sequence numbers increase within the stream.
    ValueTask Send(ReadOnlyMemory<byte> pcm, long sequence, CancellationToken ct);
    IAsyncEnumerable<AsrStreamUpdate> ReadUpdates(CancellationToken ct);
    Task Complete(CancellationToken ct);
}
