using Playback.Domain.Audio;

namespace Playback.Application.Audio;

public interface ICombinedAudioRepository
{
    Task<List<AudioPart>> Parts(string sessionId);
    Task<CombinedAudioSegment?> Find(string id);
    Task Save(CombinedAudioSegment segment);
}
public interface ICombinedAudioFiles
{
    Task<(AudioFormat Format, long Frames)> Inspect(string chunkId, CancellationToken ct);
    Task<(string Hash, string StorageKey)> Publish(CombinedAudioSegment segment, CancellationToken ct);
    Task<bool> Valid(CombinedAudioSegment segment, CancellationToken ct);
}
public sealed class CombinedAudioWorkflow(ICombinedAudioRepository repository, ICombinedAudioFiles files)
{
    public async Task Run(string sessionId, bool enabled, double maximumSeconds, string revision, CancellationToken ct)
    {
        if (!enabled) return;
        var parts = await repository.Parts(sessionId);
        var prepared = new List<AudioPart>();
        foreach (var part in parts)
        {
            if (!QuietAudio.IsQuiet(part.Status)) { prepared.Add(part); continue; }
            var audio = await files.Inspect(part.Id, ct);
            prepared.Add(part with { Format = audio.Format, Frames = audio.Frames });
        }
        foreach (var segment in QuietAudio.Plan(prepared, maximumSeconds, revision))
        {
            var saved = await repository.Find(segment.Id);
            if (saved?.State == "published" && await files.Valid(saved, ct)) continue;
            await repository.Save(segment); // A crash leaves recoverable work, never completed playback.
            try
            {
                var result = await files.Publish(segment, ct);
                segment.DerivedHash = result.Hash; segment.StorageKey = result.StorageKey; segment.State = "published";
                await repository.Save(segment);
            }
            catch { segment.State = "failed"; await repository.Save(segment); throw; }
        }
    }
}
