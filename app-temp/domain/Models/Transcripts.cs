using Playback.Domain.Notes;
using System.Text.RegularExpressions;

namespace Playback.Domain.Models;

// Transcripts: the original ASR text of each chunk and its separately saved translations.
public sealed class Transcript
{
    // ASR marks speech it could not resolve; such text needs review and is never a confident source.
    public static bool NeedsReview(string original) =>
        Regex.IsMatch(original, @"\[(?:unclear|inaudible|unintelligible)\]", RegexOptions.IgnoreCase);

    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Original { get; set; } = "";
    public string? DisplayOriginal { get; set; }
    public string? FirstPassOriginal { get; set; }
    public string ReviewStatus { get; set; } = "";
    public int ReviewAttempts { get; set; }
    public DateTime? ReviewRetryAt { get; set; }
    public string? ReviewError { get; set; }
    public DateTime? ReviewedAt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceText => DisplayOriginal ?? Original;
    public string? AsrLanguageHint { get; set; }
    public string? AsrDetectedLanguage { get; set; }
    public string? AsrProvider { get; set; }
    public string? AsrModel { get; set; }
    public string? Translation { get; set; }
    public string? TranslationLanguage { get; set; }
    public string TranslationStatus { get; set; } = "pending";
    public int TranslationAttempts { get; set; }
    public DateTime? TranslationRetryAt { get; set; }
    public string? TranslationError { get; set; }
    public string NoteStatus { get; set; } = "pending";
    public int NoteAttempts { get; set; }
    public DateTime? NoteRetryAt { get; set; }
    public string? NoteError { get; set; }
    public string? Revision { get; set; }
    public bool Uncertain { get; set; }
    public string RecognitionStatus { get; set; } = "recognized";
    public DateTime? ConfirmedAt { get; set; }
    // A rerun window re-recognizes several consecutive chunks as one segment and supersedes their rows.
    // Superseded rows stay for history and trace; only active rows are evidence. Missing fields read as an active chunk row.
    public string Kind { get; set; } = "chunk";
    public List<string> ChunkIds { get; set; } = [];
    public string? RunId { get; set; }
    public string? SupersededBy { get; set; }
    public DateTime? SupersededAt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> CoveredChunkIds => ChunkIds.Count > 0 ? ChunkIds : [Id];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Active => SupersededBy is null;
}
public sealed class TranslationVersion
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string TranscriptId { get; set; } = "";
    public string Language { get; set; } = "";
    public string Text { get; set; } = "";
    public string OriginalHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
