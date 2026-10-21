using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Playback.Api;

public static class LanguageSettings
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength,
        [Out] char[]? destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);
    public static void ValidateAsr(string language)
    {
        AsrLanguage.Validate(language);
    }

    public static string? ProviderHint(string language) => AsrLanguage.ProviderHint(language);

    public static void ValidateNote(string language) => NoteLanguage.ValidateNote(language);

    public static string OutputDescription(string language) => NoteLanguage.OutputDescription(language);

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
