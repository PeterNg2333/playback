using Playback.Domain.Models;
namespace Playback.Application.Recording;
public sealed record ChunkUpload(Dictionary<string, string> Fields, Stream Content, long Length);
public interface IChunkRepository
{
    Task<ChunkRecord> ImportChunk(ChunkUpload upload, CancellationToken ct);
    string? ChunkAudioPath(string chunkId);
    Task<string?> CombinedAudioPath(string sessionId, string segmentId);
}
public interface IAsrJobs
{
    bool Enqueue(string id, bool recovery = false);
    Task Retry(string sessionId, IEnumerable<string>? ids);
}
public interface IAudioRenderer {Task<byte[]?> Render(string sessionId, int index, string? source);}
public sealed class ChunkCommands(IChunkRepository repository, IAsrJobs jobs, IAudioRenderer renderer)
{
    public async Task<ChunkRecord> Import(ChunkUpload upload, CancellationToken ct)
    {
        if (upload.Length is < 44 or > 25000000) throw new InvalidOperationException("Expected a bounded WAV file");
        var chunk = await repository.ImportChunk(upload, ct);
        if (!ChunkStatus.Finished(chunk.Status) && chunk.Status is not (ChunkStatus.AsrError or ChunkStatus.AsrManual)) jobs.Enqueue(chunk.Id);
        return chunk;
    }
    public Task Retry(string sessionId, IEnumerable<string>? ids) => jobs.Retry(sessionId, ids);
    public string? ChunkAudioPath(string id) => repository.ChunkAudioPath(id);
    public Task<string?> CombinedAudioPath(string sessionId, string segmentId) => repository.CombinedAudioPath(sessionId, segmentId);
    public Task<byte[]?> Render(string sessionId, int index, string? source) => renderer.Render(sessionId, index, source);
}
