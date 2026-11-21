using MongoDB.Driver;

namespace Playback.Infrastructure.Storage;

// A session's export conversation: the owner's messages and the model's replies, each reply with the draft it wrote
// (as JSON) and the template it was written for. One document per session; Id is the session ID.
public sealed class ExportTurnRecord
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string? BlocksJson { get; set; }
    public string? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int NoteVersion { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class ExportConversationRecord
{
    public string Id { get; set; } = "";
    public string? OwnerId { get; set; }
    public List<ExportTurnRecord> Turns { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

public partial class PlaybackStore
{
    const int MaxExportTurns = 80;

    public async Task<List<ExportTurnRecord>> ExportTurns(string sessionId)
    {
        await RequireSessionOwner(sessionId);
        var record = await Collection<ExportConversationRecord>("export_conversations").Find(x => x.Id == sessionId).FirstOrDefaultAsync();
        return record?.Turns ?? [];
    }

    // Adds turns; the oldest go once a conversation passes MaxExportTurns.
    public async Task AddExportTurns(string sessionId, params ExportTurnRecord[] turns)
    {
        await RequireSessionOwner(sessionId);
        await Collection<ExportConversationRecord>("export_conversations").UpdateOneAsync(x => x.Id == sessionId,
            Builders<ExportConversationRecord>.Update
                .SetOnInsert(x => x.OwnerId, CurrentOwner)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .PushEach(x => x.Turns, turns, slice: -MaxExportTurns),
            new UpdateOptions { IsUpsert = true });
    }

    public async Task ClearExportTurns(string sessionId)
    {
        await RequireSessionOwner(sessionId);
        await Collection<ExportConversationRecord>("export_conversations").DeleteOneAsync(x => x.Id == sessionId);
    }
}
