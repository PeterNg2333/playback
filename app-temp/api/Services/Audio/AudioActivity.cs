using NAudio.Wave;

namespace Playback.Api.Services.Audio;

public static class AudioActivity
{
    static float Sample(ReadOnlySpan<byte> buffer, int offset, WaveFormatEncoding encoding, int bits) =>
        encoding == WaveFormatEncoding.IeeeFloat && bits == 32
            ? BitConverter.ToSingle(buffer.Slice(offset, 4))
            : encoding == WaveFormatEncoding.Pcm && bits == 16
                ? BitConverter.ToInt16(buffer.Slice(offset, 2)) / 32768f
                : encoding == WaveFormatEncoding.Pcm && bits == 24
                    ? ((buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16) << 8) / 2147483648f
                : encoding == WaveFormatEncoding.Pcm && bits == 32
                    ? BitConverter.ToInt32(buffer.Slice(offset, 4)) / 2147483648f
                : 0;

    static bool Supported(WaveFormatEncoding encoding, int bits) =>
        (encoding == WaveFormatEncoding.Pcm && bits is 16 or 24 or 32) ||
        (encoding == WaveFormatEncoding.IeeeFloat && bits == 32);

    public static int Level(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var encoding = format.AsStandardWaveFormat().Encoding;
        var bits = format.BitsPerSample;
        if (!Supported(encoding, bits)) return 0;
        var sampleBytes = bits / 8;
        var frameBytes = sampleBytes * Math.Max(1, format.Channels);
        var peak = 0f;
        for (var frame = 0; frame + frameBytes <= buffer.Length; frame += frameBytes * 4)
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sample = Sample(buffer, frame + channel * sampleBytes, encoding, bits);
                if (float.IsFinite(sample)) peak = Math.Max(peak, Math.Abs(sample));
            }
        return (int)(Math.Clamp(peak, 0, 1) * 1000);
    }

    public static float[] Mono(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var encoding = format.AsStandardWaveFormat().Encoding;
        var bits = format.BitsPerSample;
        if (!Supported(encoding, bits)) throw new NotSupportedException($"VAD cannot decode {encoding} {bits}-bit audio");
        var sampleBytes = bits / 8;
        var channels = Math.Max(1, format.Channels);
        var frameBytes = sampleBytes * channels;
        var mono = new float[buffer.Length / frameBytes];
        for (var frame = 0; frame < mono.Length; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
                sum += Sample(buffer, frame * frameBytes + channel * sampleBytes, encoding, bits);
            mono[frame] = float.IsFinite(sum) ? Math.Clamp(sum / channels, -1, 1) : 0;
        }
        return mono;
    }
    // An energy gate for immediate UI feedback, not a speech classifier.
    public static bool HasSound(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var encoding = format.AsStandardWaveFormat().Encoding;
        var bits = format.BitsPerSample;
        if (!Supported(encoding, bits)) return false;
        var sampleBytes = bits / 8;
        var frameBytes = sampleBytes * Math.Max(1, format.Channels);
        var above = 0;
        for (var frame = 0; frame + frameBytes <= buffer.Length; frame += frameBytes * 4)
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sample = Sample(buffer, frame + channel * sampleBytes, encoding, bits);
                if (float.IsFinite(sample) && Math.Abs(sample) >= 0.015f && ++above >= 8) return true;
            }
        return false;
    }
}
