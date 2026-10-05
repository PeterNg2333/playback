using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Playback.Api.Providers;

// Check provider completion before an agent response can become a saved note or answer.
public sealed class OutputGuardChatClient(IChatClient inner, Action<string>? onUsage = null) : DelegatingChatClient(inner)
{
    public static void Validate(ChatFinishReason? reason)
    {
        if (reason == ChatFinishReason.Length)
            throw new InvalidOperationException("Gemini reached the output limit; the incomplete response was not saved");
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        if (response.Usage is not null) onUsage?.Invoke(System.Text.Json.JsonSerializer.Serialize(response.Usage));
        Validate(response.FinishReason);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var truncated = false;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var usage in update.Contents.OfType<UsageContent>())
                onUsage?.Invoke(System.Text.Json.JsonSerializer.Serialize(usage.Details));
            // The SDK can emit usage after the finish-reason update. Drain it before
            // rejecting the completion so a paid, incomplete response keeps its usage.
            truncated |= update.FinishReason == ChatFinishReason.Length;
            if (!truncated) yield return update;
        }
        if (truncated) Validate(ChatFinishReason.Length);
    }
}
