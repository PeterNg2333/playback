using Playback.Api.Terms;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Api.Db;

public sealed class ActivityRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Task { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "queued";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Summary { get; set; }
    public List<string> SourceIds { get; set; } = [];
    public int? BasedOnVersion { get; set; }
    [BsonIgnore] public string? Draft { get; set; }
    public string? PromptVersion { get; set; }
    public string? PromptHash { get; set; }
    public string? InputHash { get; set; }
    public int? InputBytes { get; set; }
    public string? UsageJson { get; set; }
    public string? SectionId { get; set; }
    public long? ScheduleDelayMs { get; set; }
    public long? ProviderLatencyMs { get; set; }
    public long? QueueDelayMs { get; set; }
    public string? PromptText { get; set; }
}

public sealed record SessionView(
    string Id,
    string Title,
    string? GroupId,
    DateTime CreatedAt,
    string NoteMarkdown,
    int NoteVersion,
    long NoteProcessedThroughMs,
    bool TranslationEnabled,
    string TranslationLanguage,
    List<Material> Materials,
    List<Transcript> Transcripts,
    List<ChunkRecord> Chunks,
    List<TermCandidate> Terms,
    List<TermInsight> TermInsights,
    Note? CurrentNote,
    string AsrLanguage = "auto",
    string NoteLanguage = "zh-Hant",
    string? AsrModel = null)
{
    public List<Playback.Api.Services.Ai.TranscriptSourceGroup> SourceGroups => new Playback.Api.Services.Ai.SourceReferences(this).Groups;
}
[BsonIgnoreExtraElements]
public sealed class SessionRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? GroupId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool TranslationEnabled { get; set; }
    public string TranslationLanguage { get; set; } = "zh-Hant";
    public string AsrLanguage { get; set; } = "auto";
    public string? AsrModel { get; set; }
    public string NoteLanguage { get; set; } = "zh-Hant";
}
public sealed class GroupRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
public sealed class Material
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}
public sealed class Note
{
    [BsonId] public string Id { get; set; } = "";
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
public sealed class NoteGateRecord
{
    [BsonId] public string Id { get; set; } = "";
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
}
public sealed class Transcript
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Original { get; set; } = "";
    public string? DisplayOriginal { get; set; }
    [BsonIgnore, System.Text.Json.Serialization.JsonIgnore]
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
}
public sealed class CitationRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string Kind { get; set; } = "web";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public bool Private { get; set; } = true;
}
public sealed class TranslationVersion
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string TranscriptId { get; set; } = "";
    public string Language { get; set; } = "";
    public string Text { get; set; } = "";
    public string OriginalHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
public sealed class TermInsight
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Term { get; set; } = "";
    public string Context { get; set; } = "";
    public string OutputLanguage { get; set; } = "zh-Hant";
    public string ExplanationVersion { get; set; } = "context-v1";
    public int? AddedToNoteVersion { get; set; }
    public bool Highlight { get; set; }
    public double JevProbability { get; set; }
    public string JevRank { get; set; } = "";
    public double JevConfidence { get; set; }
    public string? JevModel { get; set; }
    public bool? JevCached { get; set; }
    public string? DecisionRule { get; set; }
    public DateTime RankedAt { get; set; }
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
    public string? Explanation { get; set; }
    public string? ExplanationSummary { get; set; }
    public List<string> SearchQueries { get; set; } = [];
    public string? GroundingUsageJson { get; set; }
    public string? GroundingMetadataJson { get; set; }
    public List<TermEvidence> Evidence { get; set; } = [];
    public DateTime? ExplainedAt { get; set; }
}
public sealed class TermEvidence
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
}
public sealed class ChunkRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long Sequence { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Hash { get; set; } = "";
    public string Status { get; set; } = "pending-asr";
    public string? Error { get; set; }
    public int AsrAttempts { get; set; }
    [BsonIgnore] public string Path { get; set; } = "";
}
