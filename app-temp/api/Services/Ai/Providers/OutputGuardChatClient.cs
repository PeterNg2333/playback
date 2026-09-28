using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Playback.Api.Services.Ai.Providers;

// Check provider completion before an agent response can become a saved note or answer.
public sealed class OutputGuardChatClient(IChatClient inner) : DelegatingChatClient(inner)
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
        Validate(response.FinishReason);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            Validate(update.FinishReason);
            yield return update;
        }
    }
}
