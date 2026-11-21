using MongoDB.Bson.Serialization;
using Playback.Domain.Models;

namespace Playback.Infrastructure.Storage;

// Keep the legacy BSON contract while the domain documents stay storage independent. Fields a class no longer has are
// skipped when old documents are read, so removing a property never breaks saved data. Null members are not written:
// a missing field reads back as null and `{field: null}` filters match it, so only the stored size changes.
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
                    new MongoDB.Bson.Serialization.Conventions.ConventionPack { new MongoDB.Bson.Serialization.Conventions.IgnoreExtraElementsConvention(true),
                        new MongoDB.Bson.Serialization.Conventions.IgnoreIfNullConvention(true) },
                    type => type.Namespace?.StartsWith("Playback", StringComparison.Ordinal) == true);
                registered = true;
            }
            if (!BsonClassMap.IsClassMapRegistered(typeof(SessionRecord))) BsonClassMap.RegisterClassMap<SessionRecord>(map => {map.AutoMap(); map.SetIgnoreExtraElements(true);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(ChunkRecord))) BsonClassMap.RegisterClassMap<ChunkRecord>(map => {map.AutoMap(); map.UnmapMember(x => x.Path);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(Transcript))) BsonClassMap.RegisterClassMap<Transcript>(map => {map.AutoMap(); map.UnmapMember(x => x.SourceText); map.UnmapMember(x => x.CoveredChunkIds); map.UnmapMember(x => x.Active);});
            // Note edits are worked out from the previous version when read (NoteChangeLog.Between).
            if (!BsonClassMap.IsClassMapRegistered(typeof(Note))) BsonClassMap.RegisterClassMap<Note>(map => {map.AutoMap(); map.UnmapMember(x => x.Edits);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(ActivityRecord))) BsonClassMap.RegisterClassMap<ActivityRecord>(map => {map.AutoMap(); map.UnmapMember(x => x.Draft);});
            if (!BsonClassMap.IsClassMapRegistered(typeof(DemoUser))) BsonClassMap.RegisterClassMap<DemoUser>(map => {map.AutoMap(); map.MapIdMember(x => x.Username);});
        }
    }
}
