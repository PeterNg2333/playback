using MongoDB.Driver;
using Playback.Application.Recording;
using Playback.Infrastructure.Audio;
namespace Playback.Infrastructure.Storage;
public partial class PlaybackStore : IUploadedStreams
{
    async Task<UploadedStreamState?> IUploadedStreams.Find(string streamId) =>
        await Collection<AudioStreamRecord>("audio_streams").Find(x => x.Id == streamId).FirstOrDefaultAsync() is { } stream
            ? new(stream.SessionId, stream.Closed) : null;
    Task IUploadedStreams.Save(string sessionId, string streamId, bool closed) => SaveAudioStream(sessionId, streamId, closed);
}
