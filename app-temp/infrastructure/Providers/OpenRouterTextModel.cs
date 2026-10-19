using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Playback.Api.Providers;

// Open-weight text models through OpenRouter's OpenAI-compatible chat endpoint, with the same output caps and
// note schema as Gemini (ModelOutputContract). Routing only uses providers that honour every sent parameter,
// so a provider without structured outputs can never answer a note request, and skips 4-bit quantizations.
public sealed class OpenRouterTextModel : IDisposable
{
    // Models run through the 2026-10-18 note comparison (docs/note-model-comparison-2026-10-18.md); another model
    // needs its own comparison run first.
    public static readonly string[] Allowed = ["qwen/qwen3.8-flash", "deepseek/deepseek-v4.1-flash", "z-ai/glm-5.3-flash"];
    // Prompt caching only hits when calls reach the same provider, so a cached model is pinned to it
    // (Qwen3.8 Flash: Alibaba; a repeated 11.8k-token prefix was served 98% from cache, 2026-10-18).
    static readonly Dictionary<string, string> CachingProvider = new() { ["qwen/qwen3.8-flash"] = "alibaba" };
    public static readonly string[] ReasoningEfforts = ["none", "minimal", "low", "medium", "high"];
    const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
    const int ResponseLimit = 2_000_000;
    static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(100);

    readonly HttpClient http;
    public string Model { get; }
    public string ReasoningEffort { get; }
    public bool IsConfigured => OpenRouterKey.Read() is not null;

    public OpenRouterTextModel(string model, string reasoningEffort = "low")
        : this(new HttpClientHandler { AllowAutoRedirect = false }, model, reasoningEffort) { }

    public OpenRouterTextModel(HttpMessageHandler handler, string model, string reasoningEffort = "low")
    {
        if (!Allowed.Contains(model)) throw new InvalidOperationException("OpenRouter text model is not allowlisted");
        if (!ReasoningEfforts.Contains(reasoningEffort)) throw new InvalidOperationException("Unknown reasoning effort");
        Model = model;
        ReasoningEffort = reasoningEffort;
        http = new(handler) { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, Action<string>? onUsage = null)
    {
        if (PlaybackEnvironment.Offline)
            throw new InvalidOperationException("External AI is disabled for offline tests");
        var key = OpenRouterKey.Read() ?? throw new InvalidOperationException("OPENROUTER_API_KEY is unavailable");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CallDeadline);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(Payload(name, instructions, prompt));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter text HTTP {(int)response.StatusCode}", null, response.StatusCode);
        return ParseReply(await ProviderResponseReader.ReadBounded(response, ResponseLimit, deadline.Token), onUsage);
    }

    public JsonObject Payload(string name, string instructions, string prompt)
    {
        var reply = ModelOutputContract.OutputLimit(name);
        var payload = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = instructions },
                new JsonObject { ["role"] = "user", ["content"] = prompt }),
            // Reasoning shares max_tokens with the reply (low effort reserves about a fifth), so the cap gets
            // half again on top; the reply keeps its own task cap and the total stays bounded.
            ["max_tokens"] = ReasoningEffort == "none" ? reply : reply * 3 / 2,
            ["reasoning"] = new JsonObject { ["effort"] = ReasoningEffort, ["exclude"] = true },
            ["provider"] = new JsonObject
            {
                ["require_parameters"] = true,
                ["quantizations"] = new JsonArray("fp8", "mxfp8", "fp16", "bf16", "fp32", "unknown")
            },
            ["usage"] = new JsonObject { ["include"] = true }
        };
        if (CachingProvider.TryGetValue(Model, out var provider)) payload["provider"]!["order"] = new JsonArray(provider);
        if (ModelOutputContract.ResponseFormat(name, prompt) is ChatResponseFormatJson { Schema: { } schema } format)
            payload["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = format.SchemaName ?? "LectureSectionPatch",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(schema.GetRawText())
                }
            };
        return payload;
    }

    // Usage (with the serving provider and OpenRouter's reported cost) is kept even when the reply is rejected.
    public static string ParseReply(byte[] bytes, Action<string>? onUsage = null)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            var record = JsonNode.Parse(usage.GetRawText())!.AsObject();
            if (root.TryGetProperty("provider", out var provider) && provider.ValueKind == JsonValueKind.String)
                record["provider"] = provider.GetString();
            onUsage?.Invoke(record.ToJsonString());
        }
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"OpenRouter text provider error {Code(error)}");
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("OpenRouter text response schema is unrecognized");
        var choice = choices[0];
        if (choice.TryGetProperty("error", out error))
            throw new InvalidOperationException($"OpenRouter text provider error {Code(error)}");
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
        if (finish == "length")
            throw new InvalidOperationException("The model reached the output limit; the incomplete response was not saved");
        if (finish is not (null or "stop"))
            throw new InvalidOperationException($"OpenRouter text reply ended with {finish}; nothing was saved");
        var text = choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String ? content.GetString() : null;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("OpenRouter returned no text");
        return text;
    }

    // Provider error messages can quote request text; only the code is surfaced.
    static string Code(JsonElement error) =>
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) ? code.ToString() : "unknown";

    public void Dispose() => http.Dispose();
}
