using NAudio.Wave;
using Playback.Api.Audio;

// The Week 3 sample audio (first 20 minutes, M4A) decoded into forty 30-second 16 kHz mono WAV chunks.
// Decoding uses Windows Media Foundation, so it runs on Windows only.
static class SampleAudio
{
    public static IEnumerable<(int Sequence, byte[] Wav, bool HasSound)> Chunks(string folder)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("M4A preview requires Windows Media Foundation");
        var path = Path.Combine(folder, "sampleAudio.m4a");
        using var reader = new MediaFoundationReader(path);
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16_000, 16, 1));
        if (reader.TotalTime < TimeSpan.FromMinutes(20))
            throw new InvalidOperationException("The sample audio clip is shorter than 20 minutes");
        var buffer = new byte[32_000];
        const int bytesPerChunk = 30 * 16_000 * 2;
        for (var sequence = 0; sequence < 40; sequence++)
        {
            using var output = new MemoryStream();
            var hasSound = false;
            using (var writer = new WaveFileWriter(output, resampler.WaveFormat))
            {
                var rawBytes = 0;
                while (rawBytes < bytesPerChunk)
                {
                    var read = resampler.Read(buffer.AsSpan(0, Math.Min(buffer.Length, bytesPerChunk - rawBytes)));
                    if (read == 0) throw new InvalidOperationException($"M4A decoder stopped at chunk {sequence}");
                    hasSound |= AudioSamples.Level(buffer.AsSpan(0, read), resampler.WaveFormat) > 15;
                    writer.Write(buffer, 0, read);
                    rawBytes += read;
                }
            }
            var wav = output.ToArray();
            if (wav.Length < bytesPerChunk + 44)
                throw new InvalidOperationException($"Invalid WAV chunk {sequence}");
            yield return (sequence, wav, hasSound);
        }
    }
}
