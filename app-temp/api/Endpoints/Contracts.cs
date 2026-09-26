namespace Playback.Api.Endpoints;

public record CreateSession(string Title, string? GroupId = null);
public record GroupInput(string Name);
public record MoveSessionInput(string? GroupId);
public record TranslationInput(bool Enabled, string Language);
public record CaptureInput(string SessionId);
public record MaterialInput(string Name, string Text);
public record NoteInput(string Markdown);
public record QuestionInput(
    string Question,
    bool UseWeb = false,
    string? TranscriptId = null,
    string? SelectedText = null,
    string? MaterialId = null);
public record ExplainInput(string Term);
public record TermInput(string Term);
public record SyntheticTranscriptInput(string ChunkId, string Text);
