using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Playback.Domain.Models;

namespace Playback.Api.Db;

public partial class PlaybackStore
{
    // Username is the stable owner ID; repeated logins reuse the same workspace.
    public Task GetOrCreateDemoUser(string username, CancellationToken cancellationToken) =>
        Collection<DemoUser>("users").UpdateOneAsync(x => x.Username == username,
            Builders<DemoUser>.Update.SetOnInsert(x => x.CreatedAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true }, cancellationToken);
}
