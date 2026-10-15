using Playback.Application.Recording;
using Microsoft.Extensions.Primitives;
namespace Playback.Api.Db;
public partial class PlaybackStore : IChunkRepository
{
    public Task<ChunkRecord> ImportChunk(ChunkUpload upload, CancellationToken ct) => SaveChunk(new FormCollection(upload.Fields.ToDictionary(x => x.Key, x => new StringValues(x.Value))), new FormFile(upload.Content, 0, upload.Length, "file", "capture.wav"), ct);
}
