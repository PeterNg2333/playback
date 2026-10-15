using MongoDB.Bson.Serialization;
using Playback.Domain.Notes;

namespace Playback.Infrastructure.Notes;

public static class NoteBsonMappings
{
    static readonly object Gate = new();
    public static void Register()
    {
        lock (Gate)
        {
            if (BsonClassMap.IsClassMapRegistered(typeof(NoteSection))) return;
            BsonClassMap.RegisterClassMap<NoteSection>(map =>
            {
                map.AutoMap();
                map.GetMemberMap(x => x.Markdown).SetShouldSerializeMethod(value => ((NoteSection)value).SchemaVersion < 2);
                map.GetMemberMap(x => x.Points).SetShouldSerializeMethod(value => ((NoteSection)value).SchemaVersion < 2);
            });
        }
    }
}
