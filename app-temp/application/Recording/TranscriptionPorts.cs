using Playback.Api.Audio.Asr;
using Playback.Domain.Models;

namespace Playback.Application.Recording;

public interface ITranscriptionRepository
{
    Task<ChunkRecord?> Chunk(string id);
    Task<Transcript?> TranscriptForChunk(string id);
    Task SetChunkStatus(string id, string status, string? error = null);
    Task<SessionRecord> SessionSettings(string id);
    Task SaveTranscript(ChunkRecord chunk, string original, AsrModel? asr = null, string? displayOriginal = null, string? languageHint = null, string? detectedLanguage = null);
    Task<bool> RecordAsrFailure(string id, string error, bool stopRetries = false);
}
public interface IPreparedSpeech : IDisposable
{
    string Path {get;}
    int Bytes {get;}
    bool HasSpeech {get;}
}
public interface ITranscriptionAudio
{
    bool IsDigitalSilence(string path);
    Task<IPreparedSpeech> Prepare(string path, CancellationToken ct);
    string Display(string original, string requested, string? detected);
}
