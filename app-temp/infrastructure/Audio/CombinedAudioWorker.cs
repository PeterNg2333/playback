using Playback.Application.Activity;
using Playback.Infrastructure.Storage;
using System.Threading.Channels;

namespace Playback.Infrastructure.Audio;

public sealed class AudioStreamRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public bool Closed { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
}

// File projection failures never turn successfully classified audio into an ASR failure.
public sealed class CombinedAudioWorker(PlaybackStore store, ILogger<CombinedAudioWorker> logger) : BackgroundService
{
    readonly Channel<string> changed = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (PlaybackEnvironment.Offline) return;
        void Changed(string id) => changed.Writer.TryWrite(id);
        store.AudioChanged += Changed;
        try
        {
            var nextRecovery = DateTime.MinValue;
            while (!ct.IsCancellationRequested)
            {
                var ids = new HashSet<string>();
                while (changed.Reader.TryRead(out var id)) ids.Add(id);
                if (DateTime.UtcNow >= nextRecovery)
                {
                    nextRecovery = DateTime.UtcNow.AddSeconds(30);
                    try { ids.UnionWith(await store.QuietAudioSessions()); } catch (Exception ex) { logger.LogWarning("Quiet recovery scan failed: {Message}", AiActivity.SafeError(ex)); }
                }
                foreach (var id in ids.Where(PlaybackEnvironment.AllowsAutomaticWork))
                    try { await store.RebuildCombinedAudio(id, ct); } catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogWarning("Quiet audio projection retained originals for {Session}: {Message}", id, AiActivity.SafeError(ex)); }
                await Task.Delay(1000, ct);
            }
        }
        finally { store.AudioChanged -= Changed; }
    }
}
