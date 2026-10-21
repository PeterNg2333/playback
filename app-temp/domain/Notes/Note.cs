namespace Playback.Domain.Notes;

// One saved version of a session's notes. Every change saves a new version; history keeps the earlier ones.
public sealed class Note
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int Version { get; set; }
    public int? BasedOnVersion { get; set; }
    public string Markdown { get; set; } = "";
    public string Author { get; set; } = "";
    public string? OutputLanguage { get; set; }
    public DateTime CreatedAt { get; set; }
    // Identifies what this version was written from, so the same request is not saved twice.
    public string? InputHash { get; set; }
    // The transcript rows and materials the notes cite.
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
    // What changed from the previous version, for the edit log.
    public List<NoteEdit> Edits { get; set; } = [];
    public List<NoteSection> Sections { get; set; } = [];
    public List<NoteCitation> Citations { get; set; } = [];
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
// A topic: a title and its blocks, under at most two levels of parent topics.
public sealed class NoteSection
{
    public string Id { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string? ParentId { get; set; }
    public int Order { get; set; }
    public List<NoteBlock> Blocks { get; set; } = [];
    // The owner edited the title; automatic writing keeps it.
    public bool UserEdited { get; set; }
    // The segment blocks a finished topic was polished from; a rewritten segment makes the topic due for polishing again.
    public List<string> SegmentBlockIds { get; set; } = [];
}
// A paragraph of notes and the transcript rows or material passages it was written from. A block the owner edited or
// locked (UserEdited) is never changed by automatic writing.
public sealed class NoteBlock
{
    public string Id { get; set; } = "";
    public int Order { get; set; }
    public string Role { get; set; } = "note";
    public string Body { get; set; } = "";
    public bool UserEdited { get; set; }
    public List<string> CitationIds { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}
// A compact citation key in a body ([cite_…]) and the sources it stands for.
public sealed class NoteCitation
{
    public string Id { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
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
