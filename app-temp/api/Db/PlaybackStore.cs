using System.Collections.Concurrent;
using MongoDB.Driver;
using System.Security.Claims;

namespace Playback.Api.Db;

// The local MongoDB store. Each file in Db/ holds one saved concept: its documents and their reads and writes.
public partial class PlaybackStore
{
    public event Action<string>? SourceChanged;
    readonly IMongoDatabase db;
    readonly IHttpContextAccessor? http;
    // Request identity only; background workers operate on session IDs without an HTTP user.
    string? CurrentOwner => http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    // One note write or language change at a time per session, so a save never mixes two bases.
    readonly ConcurrentDictionary<string, SemaphoreSlim> noteWriteLocks = new();
    readonly string audioRoot = PlaybackEnvironment.AudioFolder;
    public PlaybackStore(IHttpContextAccessor? http = null)
    {
        this.http = http;
        var settings = MongoClientSettings.FromConnectionString(PlaybackEnvironment.MongoUri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        var database = PlaybackEnvironment.Database;
        if (database is not ("playback_prototype" or "playback_e2e"))
            throw new InvalidOperationException("MongoDB database is not allowlisted");
        db = new MongoClient(settings).GetDatabase(database);
    }
    IMongoCollection<T> Collection<T>(string name) => db.GetCollection<T>(name);
    public async Task<bool> OwnsSession(string id, string owner) =>
        await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == id && x.OwnerId == owner) > 0;
    public async Task<bool> OwnsGroup(string id, string owner) =>
        await Collection<GroupRecord>("groups").CountDocumentsAsync(x => x.Id == id && x.OwnerId == owner) > 0;
    public async Task RequireSessionOwner(string id)
    {
        if (CurrentOwner is { } owner && !await OwnsSession(id, owner))
            throw new Playback.Api.Security.WorkspaceNotFoundException();
    }
    async Task RequireGroupOwner(string id)
    {
        if (CurrentOwner is { } owner && !await OwnsGroup(id, owner))
            throw new Playback.Api.Security.WorkspaceNotFoundException();
    }
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
