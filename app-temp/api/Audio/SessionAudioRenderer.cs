using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Playback.Api.Db;

namespace Playback.Api.Audio;

public sealed class SessionAudioRenderer(PlaybackStore store)
{
    public const int SegmentMs = 30_000;
    const int SampleRate = 16_000;

    public async Task<byte[]?> Render(string sessionId, int index, string? sourceId)
    {
        if (!ChunkIdentity.IsSessionId(sessionId) || index is < 0 or > 720)
            throw new InvalidOperationException("Invalid session audio segment");
        if (sourceId is not null && !ChunkIdentity.IsSourceId(sourceId))
            throw new InvalidOperationException("Invalid audio source");
        var startMs = (long)index * SegmentMs;
        var endMs = startMs + SegmentMs;
        var chunks = (await store.AudioWindowChunks(sessionId, startMs, endMs, sourceId))
            .Where(chunk => chunk.Status != ChunkStatus.Silent).ToList();
        if (chunks.Count == 0) return null;

        var renderedEndMs = Math.Min(endMs, chunks.Max(chunk => chunk.EndMs));
        var output = new float[(int)((renderedEndMs - startMs) * SampleRate / 1000)];
        var contributors = new byte[output.Length];
        var readAny = false;
        foreach (var chunk in chunks)
        {
            if (store.ChunkAudioPath(chunk.Id) is not { } path) continue;
            using var reader = new WaveFileReader(path);
            if (reader.WaveFormat.Channels is not (1 or 2))
                throw new InvalidOperationException("Only mono or stereo captured audio can be played");
            var overlapStart = Math.Max(startMs, chunk.StartMs);
            var overlapEnd = Math.Min(endMs, chunk.EndMs);
            if (overlapEnd <= overlapStart) continue;
            reader.CurrentTime = TimeSpan.FromMilliseconds(overlapStart - chunk.StartMs);
            ISampleProvider samples = reader.ToSampleProvider();
            if (reader.WaveFormat.Channels == 2)
                samples = new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f };
            if (samples.WaveFormat.SampleRate != SampleRate)
                samples = new WdlResamplingSampleProvider(samples, SampleRate);

            var destination = (int)((overlapStart - startMs) * SampleRate / 1000);
            var remaining = Math.Min(output.Length - destination, (int)((overlapEnd - overlapStart) * SampleRate / 1000));
            var buffer = new float[4096];
            while (remaining > 0)
            {
                var read = samples.Read(buffer.AsSpan(0, Math.Min(buffer.Length, remaining)));
                if (read <= 0) break;
                readAny = true;
                for (var i = 0; i < read; i++)
                {
                    output[destination + i] += buffer[i];
                    contributors[destination + i]++;
                }
                destination += read;
                remaining -= read;
            }
        }
        if (!readAny) return null;

        var pcm = new byte[output.Length * sizeof(short)];
        for (var i = 0; i < output.Length; i++)
        {
            var mixed = contributors[i] > 1 ? output[i] / contributors[i] : output[i];
            var sample = (short)Math.Round(Math.Clamp(mixed, -1f, 1f) * 32767);
            pcm[i * 2] = (byte)sample;
            pcm[i * 2 + 1] = (byte)(sample >> 8);
        }
        using var stream = new MemoryStream(pcm.Length + 44);
        using (var writer = new WaveFileWriter(stream, new WaveFormat(SampleRate, 16, 1)))
            writer.Write(pcm, 0, pcm.Length);
        return stream.ToArray();
    }
}
