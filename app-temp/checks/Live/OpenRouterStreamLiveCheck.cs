using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Application.Activity;
using Playback.Infrastructure.Providers;

// Paid, small, on a three-sentence synthetic lecture (no audio). Each mode makes one note revision through the
// production NoteWorkflow and NoteActivityAdapter (structured output + streaming + activity draft) and prints what the
// SSE stream contained (comments, events, where usage and the finish reason came, [DONE]), when the first draft
// appeared, and what the activity entry says. Needs PLAYBACK_OPENROUTER_STREAM_LIVE=yes (set by
// run-openrouter-stream-live.mjs) and the keys in the process environment.
// The writer is built like NoteWriters.Create: Qwen3.8 Flash (reasoning none), then NoteWriters.ModelFallbacks, then
// Gemini.
//   stream    as the app sends it: one OpenRouter request (Qwen).
//   models    Qwen's request is answered here with the HTTP 429 Alibaba's shared pool returned on 2026-10-19 (never
//             sent), so the fallback model writes: one OpenRouter request (GLM), no Gemini.
//   fallback  OpenRouter made unreachable through a dead local proxy (127.0.0.1:9), so three attempts fail without
//             leaving the machine and Gemini writes: one Gemini request, no OpenRouter request.
static class OpenRouterStreamLiveCheck
{
    const string Model = "qwen/qwen3.8-flash";

    public static async Task Run(string mode)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OPENROUTER_STREAM_LIVE") != "yes")
            throw new InvalidOperationException("Set PLAYBACK_OPENROUTER_STREAM_LIVE=yes to allow a small paid note call");
        if (OpenRouterKey.Read() is null) throw new InvalidOperationException("Set a real OPENROUTER_API_KEY in this process environment");
        if (mode is not ("stream" or "models" or "fallback")) throw new InvalidOperationException("Mode must be stream, models or fallback");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "no");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_NOTES", "no");

        const string id = "openrouter-stream-live";
        var store = new MemoryStore(); store.Add(id);
        var notes = new NoteStoreAdapter(store);
        var activity = new AiActivity(store);
        var recorder = new RecordingHandler(mode == "fallback"
            ? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = true, Proxy = new System.Net.WebProxy("http://127.0.0.1:9") }
            : new HttpClientHandler { AllowAutoRedirect = false },
            mode == "models" ? QwenRateLimited : null);
        var openRouter = NoteWriters.ModelFallbacks[Model].Select(x => new OpenRouterNoteWriter(new OpenRouterTextModel(recorder, x.Model, x.Reasoning)))
            .Prepend(new OpenRouterNoteWriter(new OpenRouterTextModel(recorder, Model, "none"))).ToList();
        var gemini = new GeminiLanguageModel();
        if (mode == "fallback" && !gemini.IsConfigured) throw new InvalidOperationException("Set GOOGLE_AI_STUDIO_API_KEY for the fallback mode");
        // stream: one attempt; models: Qwen then GLM; fallback: the production attempts (nothing leaves the machine).
        var writer = new FallbackNoteWriter(openRouter, new GeminiNoteWriter(mode == "fallback" ? gemini : new NoGemini()),
            FallbackNoteWriter.Default with { Attempts = mode switch { "stream" => 1, "models" => 2, _ => FallbackNoteWriter.Default.Attempts } });
        await using var workflow = new NoteWorkflow(notes, notes, writer, new NoteActivityAdapter(activity, NullLogger<NoteAgent>.Instance), null, new TestClock());
        store.Speech(id, "a", "Synthetic lecture. A kernel debugger can stop the whole operating system at a breakpoint.");
        store.Speech(id, "b", "Local kernel debugging only reads memory; it cannot set breakpoints.");
        store.Speech(id, "c", "Symbols come from the Microsoft symbol server and are cached on the local disk.");
        var watch = Stopwatch.StartNew();
        long? firstDraftMs = null;
        var drafts = 0;
        string? lastDraft = null;
        string? liveProvider = null;
        var generation = workflow.Generate(id, default);
        while (!generation.IsCompleted)
        {
            await Task.Delay(50);
            var running = (await activity.Read(id)).SingleOrDefault(x => x.Task == "Note segment" && x.Status == "running");
            if (!string.IsNullOrEmpty(running?.Draft) && running.Draft != lastDraft)
            {
                drafts++; lastDraft = running.Draft; firstDraftMs ??= watch.ElapsedMilliseconds;
                liveProvider = running.Provider + "/" + running.Model;
            }
        }
        try { await generation; }
        catch (Exception ex) { Console.WriteLine("Generation failed: " + AiActivity.SafeError(ex)); }
        var saved = store.Activity.Values.Single(x => x.Task == "Note segment");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode, status = saved.Status, saved.Provider, saved.Model, saved.Summary, totalMs = watch.ElapsedMilliseconds, firstDraftMs,
            distinctDraftsSeen = drafts, liveProviderWhileDrafting = liveProvider, usage = saved.UsageJson,
            requests = recorder.Requests, stream = mode == "fallback" ? null : recorder.Describe(),
            note = store.Notes.TryGetValue(id, out var note) ? note.Sections.Select(x => new { x.Title, blocks = x.Blocks.Select(b => b.Body) }) : null
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    // The "models" mode: a Qwen request gets the 429 OpenRouter returned for Alibaba's shared pool on 2026-10-19, here,
    // without being sent; any other model's request goes out.
    static HttpResponseMessage? QwenRateLimited(string body)
    {
        using var sent = JsonDocument.Parse(body);
        if (sent.RootElement.GetProperty("model").GetString() != Model) return null;
        return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"code":429,"message":"qwen/qwen3.8-flash is temporarily rate-limited upstream.","metadata":{"provider_name":"Alibaba","provider_error_code":"limit_burst_rate","limit_source":"upstream_provider_shared_pool","is_byok":false}}}""",
                Encoding.UTF8, "application/json")
        };
    }

    sealed class NoGemini : GeminiLanguageModel { public override bool IsConfigured => false; }

    // Keeps a copy of each streamed response body (bounded) with the time each piece arrived, to describe the stream;
    // counts requests and their outcome; can rewrite the request body (the "models" mode).
    sealed class RecordingHandler(HttpMessageHandler inner, Func<string, HttpResponseMessage?>? intercept) : DelegatingHandler(inner)
    {
        readonly List<(long Ms, string Text)> pieces = [];
        readonly Stopwatch watch = new();
        string? contentType;
        public readonly List<string> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            using (var sent = JsonDocument.Parse(body))
                Requests.Add($"model={sent.RootElement.GetProperty("model")} reasoning={sent.RootElement.GetProperty("reasoning")} " +
                    $"stream={sent.RootElement.TryGetProperty("stream", out var stream) && stream.GetBoolean()} provider={sent.RootElement.GetProperty("provider")}");
            if (intercept?.Invoke(body) is { } answered)
            {
                Requests[^1] += $" -> HTTP {(int)answered.StatusCode} answered locally, not sent";
                return answered;
            }
            watch.Restart();
            HttpResponseMessage response;
            try { response = await base.SendAsync(request, ct); }
            catch (HttpRequestException ex) { Requests[^1] += " -> " + ex.GetType().Name + ": " + ex.Message; throw; }
            Requests[^1] += " -> HTTP " + (int)response.StatusCode;
            // A refused request: keep the start of OpenRouter's explanation (the request text is synthetic).
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                Requests[^1] += " " + System.Text.RegularExpressions.Regex.Replace(error[..Math.Min(error.Length, 1500)], "\"user_id\"\\s*:\\s*\"[^\"]*\"", "\"user_id\":\"[removed]\"");
                response.Content = new StringContent(error, Encoding.UTF8, "application/json");
            }
            contentType = response.Content.Headers.ContentType?.MediaType;
            var inner = await response.Content.ReadAsStreamAsync(ct);
            var content = new StreamContent(new Tee(inner, this));
            foreach (var header in response.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = content;
            return response;
        }

        void Add(byte[] bytes, int count)
        {
            lock (pieces) if (pieces.Sum(x => x.Text.Length) < 1_000_000) pieces.Add((watch.ElapsedMilliseconds, Encoding.UTF8.GetString(bytes, 0, count)));
        }

        public object Describe()
        {
            var text = string.Concat(pieces.Select(x => x.Text));
            var lines = text.Split('\n');
            var events = lines.Where(x => x.StartsWith("data:")).Select(x => x[5..].Trim()).ToList();
            var json = events.Where(x => x != "[DONE]").Select(x => JsonNode.Parse(x)).ToList();
            var usageEvents = json.Select((x, i) => (x, i)).Where(x => x.x?["usage"] is JsonObject).ToList();
            var finishes = json.Select(x => x?["choices"]?[0]?["finish_reason"]?.ToString()).Where(x => x is not null).ToList();
            var commentTimes = new List<long>();
            foreach (var (ms, piece) in pieces) if (piece.Contains(": OPENROUTER")) commentTimes.Add(ms);
            return new
            {
                contentType,
                comments = lines.Count(x => x.StartsWith(':')),
                commentSample = lines.FirstOrDefault(x => x.StartsWith(':')),
                commentArrivalMs = commentTimes.Take(10),
                firstByteMs = pieces.FirstOrDefault().Ms,
                firstContentMs = pieces.FirstOrDefault(x => x.Text.Contains("\"content\":\"") && !x.Text.Contains("\"content\":\"\"")).Ms,
                lastByteMs = pieces.LastOrDefault().Ms,
                dataEvents = events.Count,
                endsWithDone = events.LastOrDefault() == "[DONE]",
                usageEventIndex = usageEvents.Select(x => x.i),
                usageEventIsLastJson = usageEvents.Count == 1 && usageEvents[0].i == json.Count - 1,
                usageEventChoices = usageEvents.Select(x => x.x?["choices"]?.ToJsonString()),
                finishReasons = finishes,
                errorEvents = json.Count(x => x?["error"] is not null),
                providers = json.Select(x => x?["provider"]?.ToString()).Distinct()
            };
        }

        sealed class Tee(Stream inner, RecordingHandler owner) : Stream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                var read = await inner.ReadAsync(buffer, ct);
                if (read > 0) owner.Add(buffer[..read].ToArray(), read);
                return read;
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = inner.Read(buffer, offset, count);
                if (read > 0) owner.Add(buffer[offset..(offset + read)], read);
                return read;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
