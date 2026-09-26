using NAudio.Wave;

namespace Playback.Api.Services.Audio;

public static class AudioActivity
{
    // An energy gate for device diagnostics, not a speech classifier.
    public static bool HasSound(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var encoding = format.AsStandardWaveFormat().Encoding;
        var bits = format.BitsPerSample;
        if (bits != 16 && bits != 24 && bits != 32) return false;
        var sampleBytes = bits / 8;
        var above = 0;
        for (var offset = 0; offset + sampleBytes <= buffer.Length; offset += sampleBytes * 8)
        {
            var sample = encoding == WaveFormatEncoding.IeeeFloat && bits == 32
                ? BitConverter.ToSingle(buffer.Slice(offset, 4))
                : encoding == WaveFormatEncoding.Pcm && bits == 16
                    ? BitConverter.ToInt16(buffer.Slice(offset, 2)) / 32768f
                    : encoding == WaveFormatEncoding.Pcm && bits == 24
                        ? ((buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16) << 8) / 2147483648f
                    : encoding == WaveFormatEncoding.Pcm && bits == 32
                        ? BitConverter.ToInt32(buffer.Slice(offset, 4)) / 2147483648f
                        : 0;
            if (float.IsFinite(sample) && Math.Abs(sample) >= 0.015f && ++above >= 8) return true;
        }
        return false;
    }
}
