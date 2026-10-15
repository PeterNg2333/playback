namespace Playback.Domain.Notes;

public static class NoteLanguage
{
    public static void ValidateNote(string language)
    {
        if (language is not ("zh-Hant" or "zh-Hans" or "en"))
            throw new InvalidOperationException("Note language must be zh-Hant, zh-Hans, or en");
    }

    public static string OutputDescription(string language) => language switch
    {
        "zh-Hant" => "Traditional Chinese (繁體中文), using standard written Chinese and Hong Kong terminology",
        "zh-Hans" => "Simplified Chinese (简体中文), using standard written Chinese",
        "yue-Hant" => "Cantonese (廣東話), using Traditional Chinese characters and natural written Cantonese; do not rewrite it as Mandarin",
        "en" => "English",
        "ja" => "Japanese",
        "ko" => "Korean",
        _ => throw new InvalidOperationException("Unsupported output language")
    };

}
