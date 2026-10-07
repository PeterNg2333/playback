using MongoDB.Bson.Serialization;
using Playback.Api.Db;

namespace Playback.Infrastructure.Storage;

// Keep the legacy BSON contract while the domain documents stay storage independent.
public static class DocumentMappings
{
    static readonly object Gate = new();
    public static void Register()
    {
        lock (Gate)
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(SessionRecord))) BsonClassMap.RegisterClassMap<SessionRecord>(map => {map.AutoMap(); map.SetIgnoreExtraElements(true);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(ChunkRecord))) BsonClassMap.RegisterClassMap<ChunkRecord>(map => {map.AutoMap(); map.UnmapMember(x => x.Path);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(Transcript))) BsonClassMap.RegisterClassMap<Transcript>(map => {map.AutoMap(); map.UnmapMember(x => x.SourceText);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(ActivityRecord))) BsonClassMap.RegisterClassMap<ActivityRecord>(map => {map.AutoMap(); map.UnmapMember(x => x.Draft);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(DemoUser))) BsonClassMap.RegisterClassMap<DemoUser>(map => {map.AutoMap(); map.MapIdMember(x => x.Username);});
        }
    }
}
