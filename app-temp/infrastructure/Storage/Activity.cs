using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using Playback.Domain.Models;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    public virtual Task SaveActivity(ActivityRecord item) => Collection<ActivityRecord>("ai_activity")
        .ReplaceOneAsync(x => x.Id == item.Id, item, new ReplaceOptions { IsUpsert = true });
    public virtual Task<List<ActivityRecord>> ActivityHistory(string sessionId) => Collection<ActivityRecord>("ai_activity")
        .Find(x => x.SessionId == sessionId).SortByDescending(x => x.StartedAt).Limit(100).ToListAsync();
}
