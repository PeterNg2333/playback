namespace Playback.Api.Services.Audio;

public static class AsrModelOptions
{
    public static readonly string[] OpenRouter = ["qwen/qwen3-asr-1.7b", "openai/whisper-large-v3", "openai/whisper-large-v3-turbo"];
    public static void Validate(string? model)
    {
        if (model is not null && !OpenRouter.Contains(model))
            throw new InvalidOperationException("Unsupported session ASR model");
    }
}
