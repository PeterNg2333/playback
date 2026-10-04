using System.Collections.Concurrent;
using MongoDB.Driver;

namespace Playback.Api.Db;

// The local MongoDB store. Each file in Db/ holds one saved concept: its documents and their reads and writes.
public partial class PlaybackStore
{
    public event Action<string>? SourceChanged;
    readonly IMongoDatabase db;
    // One note write or language change at a time per session, so a save never mixes two bases.
    readonly ConcurrentDictionary<string, SemaphoreSlim> noteWriteLocks = new();
    readonly string audioRoot = PlaybackEnvironment.AudioFolder;
    public PlaybackStore()
    {
        var settings = MongoClientSettings.FromConnectionString(PlaybackEnvironment.MongoUri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        var database = PlaybackEnvironment.Database;
        if (database is not ("playback_prototype" or "playback_e2e"))
            throw new InvalidOperationException("MongoDB database is not allowlisted");
        db = new MongoClient(settings).GetDatabase(database);
    }
    IMongoCollection<T> Collection<T>(string name) => db.GetCollection<T>(name);
    public async Task<bool> IsReady()
    {
        try
        {
            await db.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            return true;
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            return false;
        }
    }
}
