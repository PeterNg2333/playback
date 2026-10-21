namespace Playback.Api.Db;

// A user-requested rerun of one session: ASR over longer windows, then the notes (incremental or rebuilt).
// The window plan is persisted so a restart resumes window by window without re-uploading completed ones.
public sealed class RerunJob
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Status { get; set; } = "running";
    public bool Asr { get; set; }
    public string? AsrModel { get; set; }
    public string AsrLanguage { get; set; } = "auto";
    public int WindowSeconds { get; set; }
    public string Notes { get; set; } = "incremental";
    public string NotesStatus { get; set; } = "pending";
    public List<RerunWindow> Windows { get; set; } = [];
    public bool CancelRequested { get; set; }
    public string? Error { get; set; }
    public string Summary { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Finished => Status is "completed" or "failed" or "cancelled";
}

public sealed class RerunWindow
{
    // The transcript segment ID this window saves: "{sessionId}-window-{hash}", so citation parsers accept it.
    public string Id { get; set; } = "";
    // The derived WAV identity: "rerun_{sha256}".
    public string AudioId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string StreamId { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    // Every chunk whose audio is concatenated, quiet chunks inside the window included.
    public List<string> ChunkIds { get; set; } = [];
    // The chunks the new segment covers: the speech chunks.
    public List<string> SpeechChunkIds { get; set; } = [];
    // The active transcript rows the segment supersedes; saving requires all of them to be still active.
    public List<string> ReplacesIds { get; set; } = [];
    // pending, completed, unchanged (same wording, nothing superseded), skipped (no speech found), failed.
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public string? Model { get; set; }
}
