using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Playback.Api.Services.Audio;

public static class AsrAudioPreparer
{
    const int SampleRate = 16_000;
    const int MaxUploadBytes = 25_000_000;

    public static byte[] Prepare(string path, CancellationToken ct)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.Channels is not (1 or 2) ||
            reader.WaveFormat.SampleRate is < 8_000 or > 96_000)
            throw new InvalidOperationException("Unsupported captured audio format for ASR");

        ISampleProvider samples = reader.ToSampleProvider();
        if (reader.WaveFormat.Channels == 2)
            samples = new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (samples.WaveFormat.SampleRate != SampleRate)
            samples = new WdlResamplingSampleProvider(samples, SampleRate);

        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(SampleRate, 16, 1)))
        {
            var input = new float[4096];
            var output = new byte[input.Length * sizeof(short)];
            int read;
            while ((read = samples.Read(input.AsSpan())) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (stream.Length + read * sizeof(short) > MaxUploadBytes)
                    throw new InvalidOperationException("Normalized ASR WAV exceeds the upload limit");
                for (var i = 0; i < read; i++)
                {
                    var value = (short)Math.Round(Math.Clamp(input[i], -1f, 1f) * 32767);
                    output[i * 2] = (byte)value;
                    output[i * 2 + 1] = (byte)(value >> 8);
                }
                writer.Write(output, 0, read * sizeof(short));
            }
        }
        return stream.ToArray();
    }
}
