namespace Playback.Api.Providers;

// The output cap a text call asks for by its name. Note calls bring their own cap and schema (NoteTask).
public static class ModelOutputContract
{
    public static int OutputLimit(string name) => name.Contains("Translator", StringComparison.Ordinal) ? 4096 : 1024;
}
