namespace Playback.Domain.Audio;
public static class AsrLanguage
{
    public static void Validate(string language)
    {
        if (language is not ("auto" or "yue-en" or "yue" or "zh" or "en")) throw new InvalidOperationException("ASR language must be auto, yue-en, yue, zh, or en");
    }
    public static string? ProviderHint(string language) => language is "auto" or "yue-en" ? null : language;
}
