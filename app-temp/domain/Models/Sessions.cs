using Playback.Domain.Notes;
using Playback.Api.Terms;
using Playback.Api.Sources;
using System.Text.RegularExpressions;

namespace Playback.Api.Db;

// Sessions, the groups that hold them, their teaching materials and language settings.
public sealed record SessionView(
    string Id,
    string Title,
    string? GroupId,
    DateTime CreatedAt,
    string NoteMarkdown,
    int NoteVersion,
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
    public List<Playback.Api.Sources.TranscriptSourceGroup> SourceGroups => new Playback.Api.Sources.SourceReferences(this).Groups;
    // Superseded transcript ID → the active segment that replaced it. Transcripts holds active rows only.
    public IReadOnlyDictionary<string, string> Supersessions { get; init; } = new Dictionary<string, string>();
}
public sealed class SessionRecord
{
    public string Id { get; set; } = "";
    public string? OwnerId { get; set; }
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
    public string Id { get; set; } = "";
    public string? OwnerId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
public sealed class Material
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}
