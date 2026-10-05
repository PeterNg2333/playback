using Playback.Api.Providers;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Text.Json;

namespace Playback.Api.Db;

public sealed class ConversationRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "New conversation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ConversationTurn
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string Question { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string AnswerJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
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

public partial class PlaybackStore
{
    public async Task<ConversationRecord> CreateConversation(string sessionId)
    {
        await SessionSettings(sessionId);
        var conversation = new ConversationRecord { Id = Guid.NewGuid().ToString("N"), SessionId = sessionId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await Collection<ConversationRecord>("conversations").InsertOneAsync(conversation);
        return conversation;
    }

    public async Task<List<ConversationRecord>> Conversations(string sessionId)
    {
        await SessionSettings(sessionId);
        return await Collection<ConversationRecord>("conversations").Find(x => x.SessionId == sessionId)
            .SortByDescending(x => x.UpdatedAt).Limit(200).ToListAsync();
    }

    public async Task<ConversationRecord> RequireConversation(string sessionId, string conversationId) =>
        await Collection<ConversationRecord>("conversations").Find(x => x.SessionId == sessionId && x.Id == conversationId)
            .FirstOrDefaultAsync() ?? throw new InvalidOperationException("Conversation is not in this session");

    public async Task<List<ConversationTurn>> ConversationTurns(string sessionId, string conversationId, int limit = 200)
    {
        await RequireConversation(sessionId, conversationId);
        var turns = await Collection<ConversationTurn>("conversation_turns")
            .Find(x => x.SessionId == sessionId && x.ConversationId == conversationId)
            .SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync();
        turns.Reverse();
        return turns;
    }

    public async Task<object> Conversation(string sessionId, string conversationId)
    {
        var conversation = await RequireConversation(sessionId, conversationId);
        var turns = await ConversationTurns(sessionId, conversationId);
        return new { conversation.Id, conversation.SessionId, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt,
            turns = turns.Select(turn => new { turn.Id, turn.Question, turn.CreatedAt,
                answer = JsonSerializer.Deserialize<JsonElement>(turn.AnswerJson) }) };
    }

    public async Task<JsonElement?> SavedConversationReply(string sessionId, string conversationId, string requestId, string inputHash)
    {
        await RequireConversation(sessionId, conversationId);
        var turn = await Collection<ConversationTurn>("conversation_turns")
            .Find(x => x.Id == conversationId + ":" + requestId && x.SessionId == sessionId).FirstOrDefaultAsync();
        if (turn is null) return null;
        if (turn.InputHash != inputHash) throw new InvalidOperationException("Request ID was already used for a different question");
        return JsonSerializer.Deserialize<JsonElement>(turn.AnswerJson);
    }

    public async Task SaveConversationTurn(string sessionId, string conversationId, string requestId, string question, string inputHash, object answer)
    {
        await RequireConversation(sessionId, conversationId);
        var turn = new ConversationTurn { Id = conversationId + ":" + requestId, SessionId = sessionId,
            ConversationId = conversationId, Question = question, InputHash = inputHash, CreatedAt = DateTime.UtcNow,
            AnswerJson = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        await Collection<ConversationTurn>("conversation_turns").UpdateOneAsync(x => x.Id == turn.Id,
            Builders<ConversationTurn>.Update.SetOnInsert(x => x.SessionId, turn.SessionId)
                .SetOnInsert(x => x.ConversationId, turn.ConversationId).SetOnInsert(x => x.Question, turn.Question)
                .SetOnInsert(x => x.InputHash, turn.InputHash).SetOnInsert(x => x.AnswerJson, turn.AnswerJson)
                .SetOnInsert(x => x.CreatedAt, turn.CreatedAt), new UpdateOptions { IsUpsert = true });
        await Collection<ConversationRecord>("conversations").UpdateOneAsync(x => x.Id == conversationId && x.SessionId == sessionId,
            Builders<ConversationRecord>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow));
        await Collection<ConversationRecord>("conversations").UpdateOneAsync(x => x.Id == conversationId && x.SessionId == sessionId && x.Title == "New conversation",
            Builders<ConversationRecord>.Update.Set(x => x.Title, question[..Math.Min(question.Length, 60)]));
    }
    public async Task SaveCitations(string sessionId, string questionId, IEnumerable<WebEvidence> evidence)
    {
        var citations = evidence.Select(x => new CitationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            QuestionId = questionId,
            Kind = x.Kind,
            Url = x.Url,
            Title = x.Title,
            StartIndex = x.StartIndex,
            EndIndex = x.EndIndex
        }).ToArray();
        if (citations.Length > 0) await Collection<CitationRecord>("citations").InsertManyAsync(citations);
    }
}
