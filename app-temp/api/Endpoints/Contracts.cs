namespace Playback.Api.Endpoints;

public record CreateSession(string Title, string? GroupId = null);
public record GroupInput(string Name);
public record MoveSessionInput(string? GroupId);
public record TranslationInput(bool Enabled, string Language);
public record LanguagesInput(string AsrLanguage, string NoteLanguage, string? AsrModel = null);
public record CaptureInput(string SessionId, string SourceMode = "both");
public record MaterialInput(string Name, string Text);
public record NoteInput(string Markdown, int? BasedOnVersion = null);
public record RecoveryInput(int BasedOnVersion, List<string> SelectedIds);
public record OrganizeInput(string SectionId, int BasedOnVersion);
public record QuestionInput(
    string Question,
    bool UseWeb = false,
    string? TranscriptId = null,
    string? SelectedText = null,
    string? MaterialId = null,
    string? RequestId = null,
    string? ConversationId = null);
public record ExplainInput(string Term);
public record TermInput(string Term, string Context = "");
public record SyntheticTranscriptInput(string ChunkId, string Text);
