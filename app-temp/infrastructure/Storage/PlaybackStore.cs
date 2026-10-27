using System.Collections.Concurrent;
using MongoDB.Driver;
using System.Security.Claims;
using Playback.Application.Recording;
using Playback.Application.Translation;
using Playback.Application.Workspace;
using Playback.Domain.Models;

namespace Playback.Infrastructure.Storage;

// The local MongoDB store. Each file in Db/ holds one saved concept: its documents and their reads and writes.
public partial class PlaybackStore : IActivityRepository, IConversationRepository, ITermRepository, ITranscriptionRepository, IWorkspaceRepository, IRecordingAccess, ITestingRepository, ITranslationRepository
{
    static PlaybackStore()
    {
        DocumentMappings.Register();
    }
    public event Action<string>? SourceChanged;
    readonly IMongoDatabase? db;
    readonly IHttpContextAccessor? http;
    // Request identity only; background workers operate on session IDs without an HTTP user.
    string? CurrentOwner => http?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    // One note write or language change at a time per session, so a save never mixes two bases.
    readonly ConcurrentDictionary<string, SemaphoreSlim> noteWriteLocks = new();
    readonly string audioRoot = PlaybackEnvironment.AudioFolder;
    // Memory adapters never construct a Mongo client; an unoverridden read/write fails explicitly.
    protected PlaybackStore(bool inMemory)
    {
        if (!inMemory) throw new ArgumentException("Use the public constructor for database storage", nameof(inMemory));
    }
    public PlaybackStore(IHttpContextAccessor? http = null)
    {
        this.http = http;
        var settings = MongoClientSettings.FromConnectionString(PlaybackEnvironment.MongoUri);
        // A stopped local database is reported at once; Atlas or Firestore needs DNS SRV and TLS on the first connection.
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(settings.Servers.All(x => x.Host is "127.0.0.1" or "localhost") ? 2 : 15);
        var database = PlaybackEnvironment.Database;
        // Firestore's MongoDB-compatible database IDs cannot contain underscores, so the cloud database uses a hyphen.
        if (database is not ("playback_prototype" or "playback_e2e" or "playback-prototype"))
            throw new InvalidOperationException("MongoDB database is not allowlisted");
        db = new MongoClient(settings).GetDatabase(database);
    }
    IMongoCollection<T> Collection<T>(string name) =>
        (db ?? throw new InvalidOperationException("Database access is unavailable in the in-memory store")).GetCollection<T>(name);
    public async Task<bool> OwnsSession(string id, string owner) =>
        await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == id && x.OwnerId == owner) > 0;
    public async Task<bool> OwnsGroup(string id, string owner) =>
        await Collection<GroupRecord>("groups").CountDocumentsAsync(x => x.Id == id && x.OwnerId == owner) > 0;
    public async Task RequireSessionOwner(string id)
    {
        if (CurrentOwner is { } owner && !await OwnsSession(id, owner))
            throw new WorkspaceNotFoundException();
    }
    async Task RequireGroupOwner(string id)
    {
        if (CurrentOwner is { } owner && !await OwnsGroup(id, owner))
            throw new WorkspaceNotFoundException();
    }
    public async Task<bool> IsReady()
    {
        if (db is null) throw new InvalidOperationException("Database access is unavailable in the in-memory store");
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
