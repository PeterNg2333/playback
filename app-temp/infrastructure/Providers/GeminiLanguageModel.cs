using System.Text.Json;
using Google.GenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Playback.Api.Providers;

public class GeminiLanguageModel : ITextGenerator
{
    public const string DefaultModel = "gemini-3.1-flash-lite";
    public string Model => Environment.GetEnvironmentVariable("PLAYBACK_GEMINI_MODEL") is { Length: > 0 } model
        ? model is "gemini-3.1-flash-lite" or "gemini-3.5-flash-lite" ? model : throw new InvalidOperationException("Routine Gemini model is not allowlisted")
        : DefaultModel;
    public const string ExplanationPromptVersion = Playback.Domain.Assistance.ModelInstructions.ExplanationPromptVersion;
    public const string ExplanationInstructions = Playback.Domain.Assistance.ModelInstructions.Explanation;
    public const string SearchInstructions = Playback.Domain.Assistance.ModelInstructions.Search;
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
    public virtual bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY"));

    public virtual Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null) =>
        Run(name, instructions, prompt, ModelOutputContract.OutputLimit(name), null, ct, model, onUpdate, onUsage);

    // A note task with its own schema and output cap.
    public virtual Task<string> Complete(NoteTask task, string instructions, string prompt, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null) =>
        Run(task.Name, instructions, prompt, task.MaxOutputTokens, ResponseFormat(task), ct, null, onUpdate, onUsage);

    // A task's schema as Gemini takes it: its schema subset has no additionalProperties, so they are removed.
    public static ChatResponseFormat ResponseFormat(NoteTask task)
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse(task.Schema)!;
        void Open(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonObject value) { value.Remove("additionalProperties"); foreach (var child in value.ToList()) Open(child.Value); }
            else if (node is System.Text.Json.Nodes.JsonArray items) foreach (var child in items) Open(child);
        }
        Open(schema);
        return ChatResponseFormat.ForJsonSchema(JsonDocument.Parse(schema.ToJsonString()).RootElement, task.Name);
    }

    async Task<string> Run(string name, string instructions, string prompt, int maxOutputTokens, ChatResponseFormat? format, CancellationToken ct,
        string? model, Action<string>? onUpdate, Action<string>? onUsage)
    {
        if (PlaybackEnvironment.Offline)
            throw new InvalidOperationException("External AI is disabled for offline tests");
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY")
            ?? throw new InvalidOperationException("Vertex AI key is unavailable");
        // Vertex Express accepts an API key without project/location: https://docs.cloud.google.com/vertex-ai/generative-ai/docs/samples/googlegenaisdk-vertexai-express-mode
        using var client = new Client(vertexAI: true, apiKey: key);
        using var chat = new OutputGuardChatClient(client.AsIChatClient(model ?? Model), onUsage);
        var agent = new ChatClientAgent(chat, new ChatClientAgentOptions {
            Name = name, ChatOptions = new ChatOptions { Instructions = instructions, MaxOutputTokens = maxOutputTokens, ResponseFormat = format }
        });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(100));
        try
        {
            if (onUpdate is null)
                return (await agent.RunAsync(prompt, cancellationToken: deadline.Token)).ToString();
            var text = new System.Text.StringBuilder();
            await foreach (var update in agent.RunStreamingAsync(prompt, cancellationToken: deadline.Token))
            {
                text.Append(update.Text);
                if (text.Length > 250_000) throw new InvalidOperationException("AI output exceeded the size limit");
                onUpdate(text.ToString());
            }
            return text.ToString();
        }
        catch (ClientError ex) when (ex.Message.Contains("not been used in project", StringComparison.OrdinalIgnoreCase) ||
                                     ex.Message.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Vertex AI is disabled for this API key's Google project. Enable the Vertex AI API or use a key from an enabled project.");
        }
    }

    public Task<GroundedResult> GroundedExplain(string term, CancellationToken ct, Action<string>? onUsage = null) =>
        Grounded(term, false, ct, onUsage);
    public virtual Task<GroundedResult> GroundedSearch(string question, CancellationToken ct, Action<string>? onUsage = null) =>
        Grounded(question, true, ct, onUsage);
    async Task<GroundedResult> Grounded(string value, bool question, CancellationToken ct, Action<string>? onUsage)
    {
        if (PlaybackEnvironment.Offline)
            throw new InvalidOperationException("External search is disabled for offline tests");
        if (value.Length is < 1 or > 4000) throw new InvalidOperationException("Invalid grounded request");
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY")
            ?? throw new InvalidOperationException("Vertex AI key is unavailable");
        // Express endpoint and Google Search tool: https://docs.cloud.google.com/gemini-enterprise-agent-platform/reference/express-mode/api-reference
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://aiplatform.googleapis.com/v1/publishers/google/models/{Model}:generateContent");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = JsonContent.Create(new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = (question ? SearchInstructions : ExplanationInstructions) + "\n" + value } } } },
            generationConfig = new { maxOutputTokens = question ? 1024 : 3072 },
            tools = new[] { new { googleSearch = new { } } }
        });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Vertex grounding HTTP {(int)response.StatusCode}");
        var bytes = await ProviderResponseReader.ReadBounded(response, 1_000_000, ct);
        var result = ParseGrounding(bytes, onUsage);
        if (result.Evidence.Count == 0)
            throw new InvalidOperationException("Google Search returned no verifiable web sources; no grounded answer was saved");
        return result;
    }
    public static GroundedResult ParseGrounding(byte[] bytes, Action<string>? onUsage = null)
    {
        using var doc = JsonDocument.Parse(bytes);
        // Usage belongs to the provider attempt even if its output later fails validation.
        var usageJson = doc.RootElement.TryGetProperty("usageMetadata", out var usage) ? usage.GetRawText() : null;
        if (usageJson is not null) onUsage?.Invoke(usageJson);
        var evidence = new List<WebEvidence>();
        var answer = "";
        var suggestions = "";
        var queries = new List<string>();
        string? metadataJson = null;
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() == "MAX_TOKENS")
                throw new InvalidOperationException("Gemini reached the output limit; the incomplete answer was not saved");
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text)) answer += text.GetString();
                }
            }
            if (candidate.TryGetProperty("groundingMetadata", out var metadata))
            {
                metadataJson = metadata.GetRawText();
                if (metadata.TryGetProperty("webSearchQueries", out var searches))
                    queries.AddRange(searches.EnumerateArray().Select(x => x.GetString() ?? ""));
                if (metadata.TryGetProperty("searchEntryPoint", out var entry) &&
                    entry.TryGetProperty("renderedContent", out var rendered))
                    suggestions = rendered.GetString() ?? "";
                if (metadata.TryGetProperty("groundingChunks", out var chunks))
                {
                    foreach (var chunk in chunks.EnumerateArray())
                    {
                        if (!chunk.TryGetProperty("web", out var web) ||
                            !web.TryGetProperty("uri", out var url) ||
                            !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) ||
                            uri.Scheme != "https") continue;
                        evidence.Add(new WebEvidence(
                            "web",
                            uri.ToString(),
                            web.TryGetProperty("title", out var title) ? title.GetString() ?? uri.Host : uri.Host,
                            0, 0));
                    }
                }
            }
        }
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("Gemini grounding returned no answer");
        return new GroundedResult(answer, evidence, true, suggestions, queries,
            usageJson, metadataJson);
    }

}
