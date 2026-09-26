using Playback.Api.Terms;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Api.Db;

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
    Note? CurrentNote);
[BsonIgnoreExtraElements]
public sealed class SessionRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? GroupId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool TranslationEnabled { get; set; }
    public string TranslationLanguage { get; set; } = "zh-Hant";
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
    public long ProcessedThroughMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
    public string? InputHash { get; set; }
    public DateTime? SourceFrom { get; set; }
    public DateTime? SourceThrough { get; set; }
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
    [BsonIgnore] public string Path { get; set; } = "";
}
