using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using Playback.Domain.Models;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    readonly PromptFiles prompts = new(PlaybackEnvironment.PromptFolder);

    // The document keeps PromptHash; the instructions themselves go to PromptFiles. Older documents still carry PromptText.
    public virtual async Task SaveActivity(ActivityRecord item)
    {
        var filed = item.PromptText is { } text && item.PromptHash is { } hash && await prompts.Save(hash, text);
        await Collection<BsonDocument>("ai_activity").ReplaceOneAsync(new BsonDocument("_id", item.Id),
            ActivityDocument(item, filed), new ReplaceOptions { IsUpsert = true });
    }
    public static BsonDocument ActivityDocument(ActivityRecord item, bool promptFiled)
    {
        var document = item.ToBsonDocument();
        if (promptFiled) document.Remove(nameof(ActivityRecord.PromptText));
        return document;
    }
    public virtual async Task<List<ActivityRecord>> ActivityHistory(string sessionId)
    {
        var items = await Collection<ActivityRecord>("ai_activity")
            .Find(x => x.SessionId == sessionId).SortByDescending(x => x.StartedAt).Limit(100).ToListAsync();
        var filed = items.Where(x => x.PromptText is null && x.PromptHash is not null).ToList();
        var hashes = filed.Select(x => x.PromptHash!).Distinct().ToList();
        var texts = await Task.WhenAll(hashes.Select(prompts.Read));
        var read = hashes.Zip(texts).ToDictionary(x => x.First, x => x.Second);
        foreach (var item in filed) item.PromptText = read[item.PromptHash!];
        return items;
    }
}
