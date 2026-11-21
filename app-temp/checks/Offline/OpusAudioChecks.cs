using NAudio.Wave;
using Playback.Infrastructure.Audio;

// Finished chunks are stored as Opus: an 8-second 16 kHz speech-like WAV must come back with the same rate and sample
// count, aligned (no look-ahead shift) and close to the original, at a small fraction of the size; other formats stay WAV.
static class OpusAudioChecks
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "playback-opus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const int rate = 16_000;
            // Voiced syllables: harmonics of a moving pitch, four per second, with short pauses between them.
            var original = new short[rate * 8];
            for (var i = 0; i < original.Length; i++)
            {
                var t = (double)i / rate;
                var syllable = Math.Max(0, Math.Sin(Math.PI * 4 * t));
                var pitch = 140 + 40 * Math.Sin(2 * Math.PI * 0.5 * t);
                var voice = Enumerable.Range(1, 6).Sum(h => Math.Sin(2 * Math.PI * pitch * h * t) / h);
                original[i] = (short)(6000 * syllable * voice);
            }
            var wav = Path.Combine(folder, "chunk.wav");
            using (var writer = new WaveFileWriter(wav, new WaveFormat(rate, 16, 1))) writer.WriteSamples(original, 0, original.Length);
            var opus = Path.Combine(folder, "chunk.opus");
            Expect.That(OpusAudio.TryEncode(wav, opus), "A mono 16 kHz WAV was not encoded");
            var decodedPath = Path.Combine(folder, "decoded.wav");
            OpusAudio.Decode(opus, decodedPath);
            short[] decoded;
            using (var reader = new WaveFileReader(decodedPath))
            {
                Expect.That(reader.WaveFormat.SampleRate == rate && reader.WaveFormat.Channels == 1 && reader.WaveFormat.BitsPerSample == 16,
                    "Decoded audio changed format");
                var bytes = new byte[reader.Length]; reader.ReadExactly(bytes);
                decoded = new short[bytes.Length / 2]; Buffer.BlockCopy(bytes, 0, decoded, 0, bytes.Length);
            }
            Expect.That(decoded.Length == original.Length, $"Decoded audio has {decoded.Length} samples, not {original.Length}");
            double Correlation(int lag)
            {
                double sum = 0, a = 0, b = 0;
                for (var i = 2_000; i < original.Length - 2_000; i++)
                {
                    double x = original[i], y = decoded[i + lag];
                    sum += x * y; a += x * x; b += y * y;
                }
                return sum / Math.Sqrt(a * b);
            }
            var best = Enumerable.Range(-800, 1601).MaxBy(Correlation);
            var similarity = Correlation(0);
            var ratio = (double)new FileInfo(wav).Length / new FileInfo(opus).Length;
            Expect.That(best == 0, $"Decoded audio is shifted by {best} samples");
            Expect.That(similarity > 0.8, $"Decoded audio is too far from the original (correlation {similarity:F2})");
            Expect.That(ratio > 8, $"Opus is only {ratio:F1}x smaller than the WAV");

            var stereo = Path.Combine(folder, "stereo.wav");
            using (var writer = new WaveFileWriter(stereo, new WaveFormat(44_100, 16, 2))) writer.WriteSamples(new short[4_410], 0, 4_410);
            Expect.That(!OpusAudio.TryEncode(stereo, Path.Combine(folder, "stereo.opus")) && !File.Exists(Path.Combine(folder, "stereo.opus")),
                "A WAV Opus storage does not take was encoded");
            // Readers get a decoded WAV from a bounded cache that drops the least recently used files and a deleted session's.
            var cache = new DecodedAudioCache(Path.Combine(folder, "cache"), limitBytes: new FileInfo(wav).Length * 2);
            var session = new string('a', 32);
            string Id(int n) => $"{session}-microphone-{n}-{new string('b', 64)}";
            var firstPath = cache.Path(Id(1), opus);
            Expect.That(new FileInfo(firstPath).Length == new FileInfo(decodedPath).Length && cache.Path(Id(1), opus) == firstPath,
                "The cache did not decode once and reuse the WAV");
            File.SetLastWriteTimeUtc(firstPath, DateTime.UtcNow.AddMinutes(-5));
            cache.Path(Id(2), opus); cache.Path(Id(3), opus);
            Expect.That(!File.Exists(firstPath) && File.Exists(cache.Path(Id(3), opus)), "The cache kept more than its limit or dropped the newest file");
            cache.Remove(session);
            Expect.That(Directory.GetFiles(Path.Combine(folder, "cache")).Length == 0, "A deleted session's decoded audio stayed");

            var saved = (Stage: Environment.GetEnvironmentVariable("ENVIRONMENT"), Switch: Environment.GetEnvironmentVariable("PLAYBACK_COMPRESS_AUDIO"));
            bool Compress(string? stage, string? value)
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT", stage);
                Environment.SetEnvironmentVariable("PLAYBACK_COMPRESS_AUDIO", value);
                return Playback.Infrastructure.PlaybackEnvironment.CompressAudio;
            }
            try
            {
                Expect.That(Compress("prod", null) && Compress("dev", null) && !Compress("local", null) && !Compress(null, null) &&
                    Compress("local", "yes") && !Compress("prod", "no"), "Audio compression is not on for dev/prod and off for local recordings");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT", saved.Stage);
                Environment.SetEnvironmentVariable("PLAYBACK_COMPRESS_AUDIO", saved.Switch);
            }
            Console.WriteLine($"Opus audio checks passed: 8 s of 16 kHz speech-like audio {ratio:F0}x smaller at {OpusAudio.Bitrate / 1000} kb/s, " +
                $"same sample count, no shift, correlation {similarity:F2}; stereo 44.1 kHz stays WAV; decoded cache reused, bounded and " +
                "cleared with its session; on by default for dev/prod only (temporary folder only)");
        }
        finally { Directory.Delete(folder, true); }
    }
}
