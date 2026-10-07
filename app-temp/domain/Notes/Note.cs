namespace Playback.Domain.Notes;

public sealed class Note
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int Version { get; set; }
    public int? BasedOnVersion { get; set; }
    public string Markdown { get; set; } = "";
    public string Author { get; set; } = "";
    public string? OutputLanguage { get; set; }
    public long ProcessedThroughMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
    public List<string> InputTranscriptIds { get; set; } = [];
    public List<string> InputMaterialIds { get; set; } = [];
    public List<NoteEdit> Edits { get; set; } = [];
    public string? InputHash { get; set; }
    public DateTime? SourceFrom { get; set; }
    public DateTime? SourceThrough { get; set; }
    public List<NoteSection> Sections { get; set; } = [];
    public List<NoteCitation> Citations { get; set; } = [];
    public List<string> DeletedSectionIds { get; set; } = [];
    public List<string> SuppressedSourceIds { get; set; } = [];
    public List<SourceDisposition> Coverage { get; set; } = [];
}
public sealed class NoteSection
{
    public string Id { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Markdown { get; set; } = "";
    public bool UserEdited { get; set; }
    public List<NotePoint> Points { get; set; } = [];
    public DateTime? OrganizedAt { get; set; }
    public int? OrganizedVersion { get; set; }
}
public sealed class NotePoint
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
public sealed class NoteCitation
{
    public string Id { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
public sealed class SourceDisposition
{
    public string SourceId { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Reason { get; set; } = "";
    public List<string> PointIds { get; set; } = [];
    public string? ContentHash { get; set; }
}
public sealed class NoteEdit
{
    public string Kind { get; set; } = "";
    public int Line { get; set; }
    public string Text { get; set; } = "";
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
}
public sealed class NoteGateRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Decision { get; set; } = "";
    public double Probability { get; set; }
    public double Confidence { get; set; }
    public string Model { get; set; } = "";
    public string? UsageJson { get; set; }
    public string PromptVersion { get; set; } = "";
    public int WaitCount { get; set; }
    public int Attempts { get; set; }
    public DateTime? RetryAt { get; set; }
    public DateTime ChangedAt { get; set; }
    public bool FlushRequested { get; set; }
    public bool GenerationRequested { get; set; }
    public int FlushVersion { get; set; }
    public int EvaluatedFlushVersion { get; set; }
    public string PolicyRevision { get; set; } = "";
    public ProcessingPolicy? PolicySnapshot { get; set; }
    public DateTime? FirstPendingAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public string AdmissionReason { get; set; } = "";
    public string EvidenceHash { get; set; } = "";
    public string SettledEvidenceHash { get; set; } = "";
    public int SettledNoteVersion { get; set; }
    public string SettledLanguage { get; set; } = "";
    public string GateFailure { get; set; } = "";
}
