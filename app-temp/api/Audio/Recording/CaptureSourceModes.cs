namespace Playback.Api.Audio.Recording;

public static class CaptureSourceModes
{
    public static string[] Sources(string mode) => mode switch
    {
        "microphone" => ["microphone"],
        "system" => ["system"],
        "both" => ["microphone", "system"],
        _ => throw new InvalidOperationException("Choose Microphone, System audio, or Both for recording")
    };
}
