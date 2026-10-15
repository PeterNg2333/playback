using Playback.Application.Recording;
using Playback.Api.Audio.Vad;

namespace Playback.Api.Audio.Asr;
public sealed class TranscriptionAudio : ITranscriptionAudio
{
    public bool IsDigitalSilence(string path) => AudioSilence.IsDigitalSilence(path);
    public string Display(string original, string requested, string? detected) => LanguageSettings.CantoneseDisplay(original, requested, detected);
    public async Task<IPreparedSpeech> Prepare(string path, CancellationToken ct)
    {
        var speech = VadAudio.Prepare(path, ct);
        var upload = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"playback-vad-{Guid.NewGuid():N}.wav");
        if (speech.HasSpeech) await File.WriteAllBytesAsync(upload, speech.Wav, ct);
        return new PreparedSpeech(upload, speech.Wav.Length, speech.HasSpeech);
    }
    sealed class PreparedSpeech(string path, int bytes, bool hasSpeech) : IPreparedSpeech
    {
        public string Path => path; public int Bytes => bytes; public bool HasSpeech => hasSpeech;
        public void Dispose() {if (File.Exists(path)) File.Delete(path);}
    }
}
