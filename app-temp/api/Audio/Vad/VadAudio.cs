using Playback.Api.Audio.Asr;
using NAudio.Wave;

namespace Playback.Api.Audio.Vad;

public sealed record SpeechAudio(byte[] Wav, int FromMs, int ThroughMs, bool HasSpeech);

public static class VadAudio
{
    // Keep onset and tail context, without changing the recoverable original recording.
    public static SpeechAudio Prepare(string path, CancellationToken ct)
    {
        var wav = AsrAudioPreparer.Prepare(path, ct);
        using var reader = new WaveFileReader(new MemoryStream(wav));
        using var pcm = new MemoryStream();
        reader.CopyTo(pcm);
        var bytes = pcm.ToArray();
        using var vad = new SpeechActivityDetector();
        var first = -1; var last = 0;
        for (var offset = 0; offset < bytes.Length; offset += 1024)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(1024, bytes.Length - offset);
            var nowMs = (offset + count) / 32;
            if (!vad.Process(AudioSamples.Mono(bytes.AsSpan(offset, count), reader.WaveFormat), 16000, nowMs)) continue;
            if (first < 0) first = Math.Max(0, offset / 32 - 300);
            last = Math.Min(bytes.Length / 32, nowMs + 500);
        }
        if (first < 0) return new([], 0, bytes.Length / 32, false);
        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(output, new WaveFormat(16000, 16, 1)))
            writer.Write(bytes, first * 32, (last - first) * 32);
        return new(output.ToArray(), first, last, true);
    }
}
