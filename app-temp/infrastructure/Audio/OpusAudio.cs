using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Wave;

namespace Playback.Infrastructure.Audio;

// A finished chunk is kept as 24 kb/s Opus speech in an Ogg file, about a tenth of its WAV, and decoded back to a WAV of the
// same rate and length whenever something reads it. Only mono 16-bit PCM at a rate Opus takes is encoded; other files stay WAV.
public static class OpusAudio
{
    public const int Bitrate = 24_000;
    const string SamplesTag = "PLAYBACK_SAMPLES";
    const string RateTag = "PLAYBACK_RATE";
    const string SkipTag = "PLAYBACK_SKIP";

    public static bool CanEncode(WaveFormat format) => format.Encoding == WaveFormatEncoding.Pcm && format.Channels == 1 &&
        format.BitsPerSample == 16 && format.SampleRate is 8000 or 12000 or 16000 or 24000 or 48000;

    public static bool TryEncode(string wavPath, string opusPath)
    {
        short[] samples; int rate;
        using (var reader = new WaveFileReader(wavPath))
        {
            if (!CanEncode(reader.WaveFormat)) return false;
            rate = reader.WaveFormat.SampleRate;
            var bytes = new byte[reader.Length];
            reader.ReadExactly(bytes);
            samples = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
        }
        var encoder = OpusCodecFactory.CreateEncoder(rate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = Bitrate;
        var tags = new OpusTags();
        tags.Fields[SamplesTag] = samples.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        tags.Fields[RateTag] = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        tags.Fields[SkipTag] = encoder.Lookahead.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var temporary = opusPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = File.Create(temporary))
            {
                var ogg = new OpusOggWriteStream(encoder, output, tags, rate);
                ogg.WriteSamples(samples, 0, samples.Length);
                ogg.Finish();
            }
            File.Move(temporary, opusPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }

    public static void Decode(string opusPath, string wavPath)
    {
        using var input = File.OpenRead(opusPath);
        var probe = new OpusOggReadStream(null, input);
        var rate = int.Parse(probe.Tags.Fields[RateTag], System.Globalization.CultureInfo.InvariantCulture);
        var expected = int.Parse(probe.Tags.Fields[SamplesTag], System.Globalization.CultureInfo.InvariantCulture);
        var skip = int.Parse(probe.Tags.Fields[SkipTag], System.Globalization.CultureInfo.InvariantCulture);
        input.Position = 0;
        var ogg = new OpusOggReadStream(OpusCodecFactory.CreateDecoder(rate, 1), input);
        var samples = new List<short>(expected);
        while (ogg.HasNextPacket && ogg.DecodeNextPacket() is { } packet) samples.AddRange(packet);
        // The encoder's look-ahead delays the decoded speech and the last frame is padded: drop the one, cut the other.
        var output = samples.Skip(skip).Take(expected).ToArray();
        var bytes = new byte[expected * 2];
        Buffer.BlockCopy(output, 0, bytes, 0, output.Length * 2);
        var temporary = wavPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var writer = new WaveFileWriter(temporary, new WaveFormat(rate, 16, 1))) writer.Write(bytes, 0, bytes.Length);
            File.Move(temporary, wavPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
