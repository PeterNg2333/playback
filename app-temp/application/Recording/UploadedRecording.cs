using Playback.Application.Workspace;
using Playback.Domain.Audio;
namespace Playback.Application.Recording;
public sealed record UploadedStreamState(string SessionId, bool Closed);
public interface IUploadedStreams
{
    Task<UploadedStreamState?> Find(string streamId);
    Task Save(string sessionId, string streamId, bool closed);
}
// A recorder outside the API (the browser) uploads its chunks to /api/chunks with a stream ID it chose. While it records it
// renews the stream's lease, as the Windows recorder does; it closes the stream at pause and at Stop. Stop also asks for the
// after-lecture notes, like the Windows recorder's Stop. A closed stream stays closed, so a late renewal cannot reopen it.
public sealed class UploadedRecordingCommands(IUploadedStreams streams, IRecordingAccess access)
{
    public async Task Update(string sessionId, string streamId, bool closed, bool stop)
    {
        await access.RequireSessionOwner(sessionId);
        if (!ChunkIdentity.IsStreamId(streamId)) throw new InvalidOperationException("Invalid stream ID");
        if (stop && !closed) throw new InvalidOperationException("Stop must close the stream");
        var existing = await streams.Find(streamId);
        if (existing is not null && existing.SessionId != sessionId) throw new WorkspaceNotFoundException();
        if (existing is not { Closed: true }) await streams.Save(sessionId, streamId, closed);
        if (stop) await access.RequestNoteFlush(sessionId);
    }
}
