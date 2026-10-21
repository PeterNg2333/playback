namespace Playback.Domain.Notes;

public sealed class Note
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int Version { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Presentation { get; set; } = "numbered-hierarchy";
    public int MeaningfulRevision { get; set; }
    public List<NoteContentChange> RecentChanges { get; set; } = [];
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
    // What the segment pass wrote, five minutes at a time; the topics above are laid out from these.
    public List<NoteSegment> Segments { get; set; } = [];
    // From the final pass, when the lecture stopped.
    public List<string> Summary { get; set; } = [];
    public List<string> ReviewQuestions { get; set; } = [];
}
// Five minutes of lecture written as study notes. InputHash identifies the transcript it was written from, so a
// corrected or re-recognized transcript makes the segment due again.
public sealed class NoteSegment
{
    public int Index { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string InputHash { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
    public List<NoteBlock> Blocks { get; set; } = [];
    public List<NoteConcept> Concepts { get; set; } = [];
    public List<NoteCorrection> Corrections { get; set; } = [];
}
// A concept a segment defines or explains; Ask and topic rewrites use them to find related notes.
public sealed class NoteConcept
{
    public string Name { get; set; } = "";
    public string Definition { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
// A misrecognized word the segment pass wrote as another term, kept so a reader can check it.
public sealed class NoteCorrection
{
    public string Heard { get; set; } = "";
    public string Written { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
public sealed class NoteSection
{
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string? ParentId { get; set; }
    public int Order { get; set; }
    public List<NoteBlock> Blocks { get; set; } = [];
    string markdown = "";
    List<NotePoint> points = [];
    public string Markdown { get => SchemaVersion < 2 && Blocks.Count == 0 ? markdown : "## " + Title + "\n\n" + string.Join("\n\n", Blocks.OrderBy(x => x.Order).Select(x => x.Body)); set => markdown = value; }
    public bool UserEdited { get; set; }
    public List<NotePoint> Points { get => Blocks.Count == 0 ? points : Blocks.SelectMany(block => block.Anchors.Select(anchor => new NotePoint { Id = anchor.Id, SourceIds = anchor.SourceIds.ToList(), Text = block.Body.Substring(anchor.Offset, anchor.Length) })).ToList(); set => points = value; }
    public DateTime? OrganizedAt { get; set; }
    public int? OrganizedVersion { get; set; }
    // The segment blocks a finished topic was polished from; a rewritten segment makes the topic due for polishing again.
    public List<string> SegmentBlockIds { get; set; } = [];
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
// Stop asks for a flush: write what is left and finish. A newer Stop increments FlushVersion, so an older acknowledgement cannot clear it.
public sealed class NoteGateRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public bool FlushRequested { get; set; }
    public int FlushVersion { get; set; }
}
public sealed class NoteContentChange
{
    public int Revision { get; set; }
    public DateTime At { get; set; }
    public List<string> TopicIds { get; set; } = [];
}
