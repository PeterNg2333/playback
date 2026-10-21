namespace Playback.Domain.Audio;

public static class AsrModelOptions
{
    public const string Default = "qwen/qwen3-asr-1.7b";
    public static readonly string[] Whisper = ["openai/whisper-large-v3", "openai/whisper-large-v3-turbo"];
    public static readonly string[] OpenRouter = [Default, "qwen/qwen3-asr-0.6b", .. Whisper,
        "elevenlabs/scribe-v2", "google/gemini-3.5-transcribe"];
    public static readonly string[] Fallbacks = ["elevenlabs/scribe-v2", "google/gemini-3.5-transcribe"];

    // Whisper is selectable. Other OpenAI transcription models from old session/environment choices resolve to Qwen.
    public static string? Resolve(string? model)
    {
        var trimmed = model?.Trim();
        if (trimmed is null || !trimmed.StartsWith("openai/", StringComparison.OrdinalIgnoreCase)) return trimmed;
        return Whisper.FirstOrDefault(x => x.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }
    public static void Validate(string? model)
    {
        if (model is not null && !OpenRouter.Contains(model))
            throw new InvalidOperationException("Unsupported session ASR model");
    }
}
