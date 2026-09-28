using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Playback.Api.Services;

public static class LanguageSettings
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength,
        [Out] char[]? destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);
    public static void ValidateAsr(string language)
    {
        if (language is not ("auto" or "yue-en" or "yue" or "zh" or "en"))
            throw new InvalidOperationException("ASR language must be auto, yue-en, yue, zh, or en");
    }

    public static string? ProviderHint(string language) => language is "auto" or "yue-en" ? null : language;

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

    public static string CantoneseDisplay(string original, string requested, string? detected)
    {
        var cantonese = requested is "yue" or "yue-en" || requested == "auto" &&
            (detected?.StartsWith("yue", StringComparison.OrdinalIgnoreCase) == true ||
             string.Equals(detected, "Cantonese", StringComparison.OrdinalIgnoreCase));
        if (!cantonese || original.Length == 0) return original;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Cantonese Traditional Chinese display requires the Windows prototype runtime");
        // Script conversion only. This does not translate Cantonese to Mandarin or repair ASR errors.
        const uint traditionalChinese = 0x04000000;
        var size = LCMapStringEx("zh-Hant", traditionalChinese, original, original.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Traditional Chinese conversion failed");
        var output = new char[size];
        var written = LCMapStringEx("zh-Hant", traditionalChinese, original, original.Length, output, size, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (written == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Traditional Chinese conversion failed");
        return new string(output, 0, written);
    }
}
