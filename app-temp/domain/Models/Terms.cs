using Playback.Domain.Notes;
using Playback.Api.Terms;
using Playback.Api.Sources;
using System.Text.RegularExpressions;

namespace Playback.Api.Db;

// Key-term decisions and their saved explanations.
public sealed class TermInsight
{
    // One explanation per term, meaning (its context) and output language within a session.
    public static string IdFor(string sessionId, string term, string context = "", string language = "zh-Hant") =>
        ContentHash.Of(context.Length == 0 ? $"{sessionId}\n{term.Trim().ToLowerInvariant()}" :
            $"{sessionId}\n{term.Trim().ToLowerInvariant()}\n{context}\n{language}\ncontext-v1");

    public string Id { get; set; } = "";
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
