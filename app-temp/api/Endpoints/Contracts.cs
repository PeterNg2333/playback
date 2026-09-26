namespace Playback.Api.Endpoints;

public record CreateSession(string Title, string? GroupId = null);
public record GroupInput(string Name);
public record MoveSessionInput(string? GroupId);
public record ConsentInput(bool Confirmed);
public record TranslationInput(bool Enabled, string Language, bool ConsentConfirmed = false);
public record CaptureInput(string SessionId, bool ConsentConfirmed = false);
public record MaterialInput(string Name, string Text);
public record NoteInput(string Markdown);
public record QuestionInput(
    string Question,
    bool UseWeb = false,
    bool WebConsentConfirmed = false,
    string? TranscriptId = null,
    string? SelectedText = null,
    string? MaterialId = null);
public record ExplainInput(string Term, bool ConsentConfirmed = false);
public record TermInput(string Term, bool ConsentConfirmed = false);
public record SyntheticTranscriptInput(string ChunkId, string Text);
