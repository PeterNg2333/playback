using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// Evidence read projection for notes. Audio, ownership, ASR and translation documents stay in their adapters.
public sealed record NoteSession(string Id, string NoteMarkdown, int NoteVersion, string NoteLanguage,
    Note? CurrentNote, List<NoteTranscript> Transcripts, List<NoteMaterial> Materials,
    List<string> TermReferenceIds, bool AwaitingTranscripts)
{
    public IEnumerable<string> AllowedSources => MaterialSources.Allowed(this);
    public List<NoteSection> Sections => CurrentNote?.Sections ?? [];
    public List<PendingNoteAudio> PendingAudio { get; init; } = [];
    // Superseded transcript ID → its replacing segment; historical versions are read through it.
    public IReadOnlyDictionary<string, string> Supersessions { get; init; } = new Dictionary<string, string>();
}
public sealed record PendingNoteAudio(string SourceId, long EndMs);

public sealed class NoteTranscript
{
    public string Id { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string SourceText { get; set; } = "";
    public bool Uncertain { get; set; }
    public string NoteStatus { get; set; } = "pending";
    public DateTime? ConfirmedAt { get; set; }
    public bool Retranscribed { get; set; }
}

public sealed class NoteMaterial
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}
