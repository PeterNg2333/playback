using Playback.Domain.Notes;
using System.Text.RegularExpressions;

namespace Playback.Domain.Models;

public sealed class ConversationRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "New conversation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ConversationTurn
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string Question { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string AnswerJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class CitationRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string Kind { get; set; } = "web";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public bool Private { get; set; } = true;
}
