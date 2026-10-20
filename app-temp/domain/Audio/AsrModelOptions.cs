namespace Playback.Api.Audio.Asr;

public static class AsrModelOptions
{
    public const string Default = "qwen/qwen3-asr-1.7b";
    public static readonly string[] OpenRouter = [Default, "qwen/qwen3-asr-0.6b",
        "elevenlabs/scribe-v2", "google/gemini-3.5-transcribe"];
    public static readonly string[] Fallbacks = ["elevenlabs/scribe-v2", "google/gemini-3.5-transcribe"];

    // Old session/environment choices must never send audio to an OpenAI model.
    public static string? Resolve(string? model) => model?.Trim().StartsWith("openai/", StringComparison.OrdinalIgnoreCase) == true
        ? Default : model?.Trim();
    public static void Validate(string? model)
    {
        if (model is not null && !OpenRouter.Contains(model))
            throw new InvalidOperationException("Unsupported session ASR model");
    }
}
