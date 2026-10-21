using Playback.Domain.Notes;
using System.Text.RegularExpressions;

namespace Playback.Domain.Models;

// Saved audio chunks: the WAV on disk, its identity, and its ASR status.
public sealed class ChunkRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string? StreamId { get; set; }
    public long Sequence { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Hash { get; set; } = "";
    public string Status { get; set; } = ChunkStatus.PendingAsr;
    public string? Error { get; set; }
    public int AsrAttempts { get; set; }
    public string Path { get; set; } = "";
}

// Where a chunk is in transcription. The web shows these values as they are.
