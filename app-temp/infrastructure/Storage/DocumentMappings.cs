using MongoDB.Bson.Serialization;
using Playback.Api.Db;

namespace Playback.Infrastructure.Storage;

// Keep the legacy BSON contract while the domain documents stay storage independent. Fields a class no longer has are
// skipped when old documents are read, so removing a property never breaks saved data.
public static class DocumentMappings
{
    static readonly object Gate = new();
    static bool registered;
    public static void Register()
    {
        lock (Gate)
        {
            if (!registered)
            {
                MongoDB.Bson.Serialization.Conventions.ConventionRegistry.Register("ignore removed fields",
                    new MongoDB.Bson.Serialization.Conventions.ConventionPack { new MongoDB.Bson.Serialization.Conventions.IgnoreExtraElementsConvention(true) },
                    type => type.Namespace?.StartsWith("Playback", StringComparison.Ordinal) == true);
                registered = true;
            }
            if (!BsonClassMap.IsClassMapRegistered(typeof(SessionRecord))) BsonClassMap.RegisterClassMap<SessionRecord>(map => {map.AutoMap(); map.SetIgnoreExtraElements(true);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(ChunkRecord))) BsonClassMap.RegisterClassMap<ChunkRecord>(map => {map.AutoMap(); map.UnmapMember(x => x.Path);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(Transcript))) BsonClassMap.RegisterClassMap<Transcript>(map => {map.AutoMap(); map.UnmapMember(x => x.SourceText); map.UnmapMember(x => x.CoveredChunkIds); map.UnmapMember(x => x.Active);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(ActivityRecord))) BsonClassMap.RegisterClassMap<ActivityRecord>(map => {map.AutoMap(); map.UnmapMember(x => x.Draft);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(DemoUser))) BsonClassMap.RegisterClassMap<DemoUser>(map => {map.AutoMap(); map.MapIdMember(x => x.Username);});
        }
    }
}
