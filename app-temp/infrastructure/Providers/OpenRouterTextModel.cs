using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Playback.Api.Providers;

// Open-weight text models through OpenRouter's OpenAI-compatible chat endpoint, with the same output caps and
// note schema as Gemini (ModelOutputContract). Routing only uses providers that honour every sent parameter,
// so a provider without structured outputs can never answer a note request, and skips 4-bit quantizations.
//
// A caller that shows the reply as it arrives (onUpdate) gets it streamed as server-sent events; otherwise the reply
// is one JSON body. Both read at most ResponseLimit bytes from the fixed endpoint. OpenRouter behaviour this relies on
// (https://openrouter.ai/docs/api-reference/streaming, /docs/api-reference/errors, /docs/features/structured-outputs,
// /docs/use-cases/usage-accounting, read 2026-10-19):
// - structured outputs stream as partial JSON that completes to the schema; response healing is not applied to streams.
// - ": OPENROUTER PROCESSING" comment lines keep a quiet stream alive; "data: [DONE]" ends it.
// - usage (with cost and cached tokens) arrives in the last chunk before [DONE], always; usage.include is deprecated.
// - an error before the first byte is a non-200 JSON body (Retry-After may come with 429 and 503); an error after it
//   is a 200 stream event with a top-level "error" and finish_reason "error", and the stream then ends.
// - closing the connection cancels the request, but some providers (Alibaba, which serves Qwen3.8 Flash here) still
//   finish and bill the whole reply.
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
    // The live draft is re-rendered from the whole reply, so it is refreshed at most this often while streaming.
    static readonly TimeSpan DraftInterval = TimeSpan.FromMilliseconds(250);

    // Call: the whole request, headers to last byte. Silence: the longest gap between bytes of a stream; OpenRouter's
    // keep-alive comments count as bytes, so a longer gap means the connection or provider stalled.
    public sealed record Limits(TimeSpan Call, TimeSpan Silence);
    public static readonly Limits DefaultLimits = new(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(45));

    readonly HttpClient http;
    readonly Limits limits;
    public string Model { get; }
    public string ReasoningEffort { get; }
    public bool IsConfigured => OpenRouterKey.Read() is not null;

    public OpenRouterTextModel(string model, string reasoningEffort = "low")
        : this(new HttpClientHandler { AllowAutoRedirect = false }, model, reasoningEffort) { }

    public OpenRouterTextModel(HttpMessageHandler handler, string model, string reasoningEffort = "low", Limits? limits = null)
    {
        if (!Allowed.Contains(model)) throw new InvalidOperationException("OpenRouter text model is not allowlisted");
        if (!ReasoningEfforts.Contains(reasoningEffort)) throw new InvalidOperationException("Unknown reasoning effort");
        Model = model;
        ReasoningEffort = reasoningEffort;
        this.limits = limits ?? DefaultLimits;
        // The per-call deadline below bounds the request; HttpClient's own timeout only backs it up.
        http = new(handler) { Timeout = this.limits.Call + TimeSpan.FromSeconds(20) };
    }

    // A failure worth another attempt (or another provider): a timeout or stall, a dropped connection, HTTP 408, 429 or
    // 5xx, or a provider error reported inside a 200 reply. Invalid requests, keys, credits, refusals, truncated or
    // filtered replies are not: repeating them gives the same answer.
    public static bool IsTransient(Exception error) => error is OpenRouterTransientException or TimeoutException;

    public async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct,
        Action<string>? onUsage = null, Action<string>? onUpdate = null)
    {
        if (PlaybackEnvironment.Offline)
            throw new InvalidOperationException("External AI is disabled for offline tests");
        var key = OpenRouterKey.Read() ?? throw new InvalidOperationException("OPENROUTER_API_KEY is unavailable");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(limits.Call);
        using var silence = new CancellationTokenSource();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, silence.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var payload = Payload(name, instructions, prompt);
            if (onUpdate is not null) payload["stream"] = true;
            request.Content = JsonContent.Create(payload);
            var since = Stopwatch.StartNew();
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token); }
            catch (HttpRequestException ex) when (ex.StatusCode is null)
            {
                throw new OpenRouterTransientException("OpenRouter could not be reached", null);
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode) throw StatusError(response);
                if (onUpdate is not null && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                    return await ReadStream(response, onUpdate, onUsage, silence, since, read.Token);
                return ParseReply(await ProviderResponseReader.ReadBounded(response, ResponseLimit, deadline.Token), onUsage);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(silence.IsCancellationRequested
                ? $"OpenRouter stream was silent for {limits.Silence.TotalSeconds:0} s"
                : $"OpenRouter text call exceeded {limits.Call.TotalSeconds:0} s");
        }
        catch (IOException) when (!ct.IsCancellationRequested)
        {
            throw new OpenRouterTransientException("OpenRouter connection dropped before the reply finished", null);
        }
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
            }
        };
        // "order" names the preferred provider; other providers of the model stay allowed (allow_fallbacks defaults to
        // true, https://openrouter.ai/docs/guides/routing/provider-selection), though Qwen3.8 Flash has no other.
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
        ReportUsage(root, onUsage);
        if (root.TryGetProperty("error", out var error)) throw ProviderError(error, streamed: false);
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("OpenRouter text response schema is unrecognized");
        var choice = choices[0];
        if (choice.TryGetProperty("error", out error)) throw ProviderError(error, streamed: false);
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
        var text = choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String ? content.GetString() : null;
        return Finished(finish ?? "stop", text);
    }

    // Server-sent events: "data:" lines (several join with a newline) make one event, a blank line ends it, lines
    // starting with ":" are comments. Each event is one chat.completion.chunk whose delta extends the reply.
    // The usage report of a stream also says how the stream went ("stream": headers, first content, keep-alive comments,
    // the longest gap between lines, all in ms from the request), so the silence limit can be checked against real calls.
    async Task<string> ReadStream(HttpResponseMessage response, Action<string> onUpdate, Action<string>? onUsage,
        CancellationTokenSource silence, Stopwatch since, CancellationToken ct)
    {
        var headersMs = since.ElapsedMilliseconds;
        long? firstContentMs = null;
        long longestGapMs = 0, lastLineMs = headersMs;
        var keepAlives = 0;
        string? usage = null;
        await using var body = new BoundedStream(await response.Content.ReadAsStreamAsync(ct), ResponseLimit);
        using var reader = new StreamReader(body, Encoding.UTF8);
        var text = new StringBuilder();
        var data = new StringBuilder();
        string? finish = null;
        var shown = 0;
        var sinceDraft = Stopwatch.StartNew();
        var first = true;
        try
        {
            while (true)
            {
                silence.CancelAfter(limits.Silence);
                var line = await reader.ReadLineAsync(ct);
                var now = since.ElapsedMilliseconds;
                longestGapMs = Math.Max(longestGapMs, now - lastLineMs);
                lastLineMs = now;
                if (line is null || line.Length == 0)
                {
                    if (data.Length > 0)
                    {
                        var payload = data.ToString();
                        data.Clear();
                        if (payload == "[DONE]") break;
                        finish = Chunk(payload, text, report => usage = report) ?? finish;
                        if (text.Length > 0) firstContentMs ??= now;
                        if (text.Length > shown && (first || sinceDraft.Elapsed >= DraftInterval))
                        {
                            onUpdate(text.ToString());
                            shown = text.Length;
                            first = false;
                            sinceDraft.Restart();
                        }
                    }
                    if (line is null) break;
                    continue;
                }
                if (line[0] == ':') { keepAlives++; continue; }
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line.AsSpan(line.Length > 5 && line[5] == ' ' ? 6 : 5));
                }
            }
        }
        finally
        {
            if (usage is not null && onUsage is not null)
            {
                var record = JsonNode.Parse(usage)!.AsObject();
                record["stream"] = new JsonObject
                {
                    ["headersMs"] = headersMs, ["firstContentMs"] = firstContentMs, ["keepAlives"] = keepAlives,
                    ["longestGapMs"] = longestGapMs, ["totalMs"] = since.ElapsedMilliseconds
                };
                onUsage(record.ToJsonString());
            }
        }
        if (finish is null) throw new OpenRouterTransientException("OpenRouter stream ended before the reply finished", null);
        var reply = Finished(finish, text.ToString());
        if (text.Length > shown) onUpdate(reply);
        return reply;
    }

    // One streamed chunk: report usage, fail on an error event, append the content delta; returns its finish reason.
    static string? Chunk(string payload, StringBuilder text, Action<string>? onUsage)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(payload); }
        catch (JsonException) { throw new InvalidOperationException("OpenRouter stream event is not JSON"); }
        using (json)
        {
            var root = json.RootElement;
            ReportUsage(root, onUsage);
            if (root.TryGetProperty("error", out var error)) throw ProviderError(error, streamed: true);
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return null;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object &&
                delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                text.Append(content.GetString());
            var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
            if (finish == "error") throw new OpenRouterTransientException("OpenRouter provider failed during the reply", null);
            return finish;
        }
    }

    static string Finished(string finish, string? text)
    {
        if (finish == "length")
            throw new InvalidOperationException("The model reached the output limit; the incomplete response was not saved");
        if (finish != "stop")
            throw new InvalidOperationException($"OpenRouter text reply ended with {finish}; nothing was saved");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("OpenRouter returned no text");
        return text;
    }

    static void ReportUsage(JsonElement root, Action<string>? onUsage)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        var record = JsonNode.Parse(usage.GetRawText())!.AsObject();
        // Who answered: the serving provider and the model as OpenRouter names it.
        if (root.TryGetProperty("provider", out var provider) && provider.ValueKind == JsonValueKind.String)
            record["provider"] = provider.GetString();
        if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
            record["model"] = model.GetString();
        onUsage?.Invoke(record.ToJsonString());
    }

    static Exception StatusError(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var message = $"OpenRouter text HTTP {status}";
        if (status is 408 or 429 || status >= 500)
            return new OpenRouterTransientException(message, RetryAfter(response), response.StatusCode);
        return new HttpRequestException(message, null, response.StatusCode);
    }

    static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow,
        _ => null
    };

    // Provider error messages can quote request text; only the code is surfaced. A provider error after the stream
    // began is the provider failing mid-reply (OpenRouter documents "server_error"), so it is transient unless its code
    // says the request itself was refused.
    static Exception ProviderError(JsonElement error, bool streamed)
    {
        var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var value) ? value : default;
        var label = code.ValueKind == JsonValueKind.Undefined ? "unknown" : code.ToString();
        var message = $"OpenRouter text provider error {label}";
        var transient = code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number)
            ? number is 408 or 429 || number >= 500
            : streamed || label is "server_error" or "timeout";
        return transient ? new OpenRouterTransientException(message, null) : new InvalidOperationException(message);
    }

    public void Dispose() => http.Dispose();

    // Counts every byte read so neither a reply nor a stream can exceed the limit.
    sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        long total;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await inner.ReadAsync(buffer, ct);
            if ((total += read) > limit) throw new InvalidOperationException("Provider response exceeds limit");
            return read;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if ((total += read) > limit) throw new InvalidOperationException("Provider response exceeds limit");
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => total; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); await base.DisposeAsync(); }
    }
}

// A failure that may succeed when repeated (see OpenRouterTextModel.IsTransient). RetryAfter is the provider's own
// Retry-After, when it sent one.
public sealed class OpenRouterTransientException(string message, TimeSpan? retryAfter, HttpStatusCode? status = null)
    : HttpRequestException(message, null, status)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
