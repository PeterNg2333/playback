using NAudio.Wave;
using Playback.Api.Audio.Asr.Providers;

// Paid: sends a 30-second 48 kHz stereo tone to SenseVoice and expects real duration and inference metrics.
// Needs PLAYBACK_ASR_LIVE_CHECK=yes (set by run-asr-live.mjs).
static class AsrLiveCheck
{
    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_ASR_LIVE_CHECK") != "yes")
            throw new InvalidOperationException("Set PLAYBACK_ASR_LIVE_CHECK=yes for the synthetic live ASR check");
        var wav = Path.Combine(Path.GetTempPath(), $"playback-synthetic-asr-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(wav, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)))
            {
                var samples = new float[48_000 * 2];
                var bytes = new byte[samples.Length * sizeof(float)];
                for (var second = 0; second < 30; second++)
                {
                    for (var i = 0; i < 48_000; i++)
                        samples[i * 2] = samples[i * 2 + 1] = 0.1f * MathF.Sin(2 * MathF.PI * 440 * i / 48_000);
                    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                    writer.Write(bytes, 0, bytes.Length);
                }
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var result = await new SenseVoiceClient().Transcribe(wav, timeout.Token);
            Expect.That(result.DurationSeconds is >= 29 and <= 31 && result.InferenceSeconds is >= 0 && result.RealTimeFactor is >= 0,
                "SenseVoice live response did not include valid duration and inference metrics");
            Console.WriteLine($"Synthetic stereo ASR upload passed: {result.Text.Length} text characters, provider inference {result.InferenceSeconds:F2} s");
        }
        finally { File.Delete(wav); }
    }
}
