using NAudio.Wave;

namespace Playback.Api.Services.Audio;

public static class AudioSilence
{
    // Only skip exact digital silence. An energy threshold could discard quiet speech.
    public static bool IsDigitalSilence(string path)
    {
        using var wav = new WaveFileReader(path);
        var format = wav.WaveFormat;
        if (wav.Length == 0 || format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
            || format.BitsPerSample is not (16 or 24 or 32)) return false;
        var buffer = new byte[16_384];
        int read;
        while ((read = wav.Read(buffer, 0, buffer.Length)) > 0)
            for (var index = 0; index < read; index++)
                if (buffer[index] != 0) return false;
        return true;
    }

}
