using System.Text.Json;
using Playback.Api.Db;
using Playback.Api.Providers;
using Playback.Api.Terms;

namespace Playback.Application.Assistance;

public interface ITextGenerator
{
    bool IsConfigured { get; }
    string Model { get; }
    Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null);
    Task<GroundedResult> GroundedSearch(string question, CancellationToken ct, Action<string>? onUsage = null);
    Task<GroundedResult> GroundedExplain(string term, CancellationToken ct, Action<string>? onUsage = null);
}
public interface IActivityRepository
{
    Task SaveActivity(ActivityRecord item);
    Task<List<ActivityRecord>> ActivityHistory(string sessionId);
    Task DeleteSession(string id);
}
public interface IConversationRepository
{
    Task<SessionView?> Session(string id);
    Task<JsonElement?> SavedConversationReply(string sessionId, string conversationId, string requestId, string inputHash);
    Task<List<ConversationTurn>> ConversationTurns(string sessionId, string conversationId, int limit = 200);
    Task SaveConversationTurn(string sessionId, string conversationId, string requestId, string question, string inputHash, object answer);
    Task SaveCitations(string sessionId, string questionId, IEnumerable<WebEvidence> evidence);
    Task<ConversationRecord> CreateConversation(string sessionId);
    Task<List<ConversationRecord>> Conversations(string sessionId);
    Task<object> Conversation(string sessionId, string conversationId);
}
public interface ITermRepository
{
    Task<SessionView?> Session(string id);
    Task<TermInsight?> FindTermInsight(string sessionId, string insightId);
    Task<TermInsight> SaveTermRanking(string sessionId, TermCandidate candidate, JevRankResult rank, string language = "zh-Hant");
    Task<TermInsight> SaveTermExplanation(string sessionId, string insightId, GroundedResult result, bool detail = false);
}
public interface ITermClassifier
{
    bool IsConfigured { get; }
    Task<JevRankResult> Rank(string term, CancellationToken ct, string context = "");
}
