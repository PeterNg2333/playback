using System.Net;
using System.Text;
using System.Text.Json;
using Playback.Api.Notes;
using Playback.Api.Providers;

// OpenRouter text models: the fixed endpoint and process key, the shared note schema and output caps, routing
// that requires every parameter and skips 4-bit quantizations, usage kept on rejected replies, and errors that
// never echo provider text. Every HTTP call is answered in memory.
static class OpenRouterTextChecks
{
    const string NoteInput = """
        {"pending":[{"id":"T001"}],"materials":[{"id":"T002"}],"sectionSources":[{"id":"T003"}],"sectionMaterials":[],
        "editableSections":[{"id":"known-section","points":[{"sourceIds":["T003"]}]}],"citationSources":[]}
        """;
    const string NoteReply = """
        {"sections":[{"id":"new","title":"TCP","points":[{"text":"TCP 用三次握手建立連線。","sourceIds":["T001"],"retains":[]}]}],"deferred":[]}
        """;

    public static async Task Run()
    {
        Expect.Rejects(() => new OpenRouterTextModel("openai/gpt-6-luna"), "A model outside the benchmarked allowlist was accepted");
        Expect.Rejects(() => new OpenRouterTextModel("deepseek/deepseek-v4.1-flash", "extreme"), "An unknown reasoning effort was accepted");
        var oldKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var oldOffline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        try
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "fixture-key");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            await NoteRequest();
            await RoutineRequests();
            await RejectedReplies();
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "your_openrouter_api_key_here");
            await ExpectNoCall("A placeholder key reached the provider");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "fixture-key");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
            await ExpectNoCall("The offline switch did not stop a text call");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", oldOffline);
        }
        Console.WriteLine("OpenRouter text checks passed (in-memory HTTP): fixed endpoint and key, note schema and caps, parameter-complete fp8+ routing, usage on rejection, safe errors, offline guard");
    }

    static async Task NoteRequest()
    {
        var handler = new StubHandler(async (message, ct) =>
        {
            Expect.That(message.Method == HttpMethod.Post && message.RequestUri?.AbsoluteUri == "https://openrouter.ai/api/v1/chat/completions",
                "Text models must use the fixed chat endpoint");
            Expect.That(message.Headers.Authorization is { Scheme: "Bearer", Parameter: "fixture-key" }, "Text models must use the process key");
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            var messages = root.GetProperty("messages");
            Expect.That(root.GetProperty("model").GetString() == "deepseek/deepseek-v4.1-flash" &&
                messages[0].GetProperty("role").GetString() == "system" && messages[0].GetProperty("content").GetString() == "Write notes." &&
                messages[1].GetProperty("content").GetString() == NoteInput, "Instructions and input must reach the model unchanged");
            Expect.That(root.GetProperty("max_tokens").GetInt32() == 8192 * 3 / 2, "Note replies keep their cap plus bounded reasoning room");
            Expect.That(root.GetProperty("reasoning").GetProperty("effort").GetString() == "low" &&
                root.GetProperty("reasoning").GetProperty("exclude").GetBoolean(), "Reasoning effort must be explicit and its text not returned");
            var provider = root.GetProperty("provider");
            var quantizations = provider.GetProperty("quantizations").EnumerateArray().Select(x => x.GetString()).ToList();
            Expect.That(provider.GetProperty("require_parameters").GetBoolean() && quantizations.Contains("fp8") &&
                !quantizations.Any(x => x is "fp4" or "int4" or "mxfp4" or "nvfp4"), "Routing must require every parameter and skip 4-bit quantizations");
            var format = root.GetProperty("response_format");
            var schema = format.GetProperty("json_schema").GetProperty("schema");
            var section = schema.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("properties");
            var sectionIds = section.GetProperty("id").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
            var sourceIds = section.GetProperty("points").GetProperty("items").GetProperty("properties").GetProperty("sourceIds")
                .GetProperty("items").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
            var deferred = schema.GetProperty("properties").GetProperty("deferred").GetProperty("items").GetProperty("properties")
                .GetProperty("sourceId").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
            Expect.That(format.GetProperty("type").GetString() == "json_schema" && format.GetProperty("json_schema").GetProperty("strict").GetBoolean() &&
                sectionIds.SequenceEqual(["new", "known-section"]) && sourceIds.SequenceEqual(["T001", "T002", "T003"]) &&
                deferred.SequenceEqual(["T001", "T002"]), "Notes must send the same constrained section schema as Gemini");
            return Reply(Completion(NoteReply, "stop"));
        });
        var usage = new List<string>();
        using var model = new OpenRouterTextModel(handler, "deepseek/deepseek-v4.1-flash");
        var patch = await new OpenRouterNoteWriter(model).Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None,
            onUsage: usage.Add);
        Expect.That(patch.Sections.Single().Points.Single().SourceIds.Single() == "T001", "A structured note reply was not parsed");
        Expect.That(usage.Single().Contains("\"provider\":\"DeepInfra\"") && usage.Single().Contains("\"cost\""),
            "Usage must keep the serving provider and reported cost");
    }

    static async Task RoutineRequests()
    {
        var caps = new List<int>();
        var formats = new List<bool>();
        var handler = new StubHandler(async (message, ct) =>
        {
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            caps.Add(body.RootElement.GetProperty("max_tokens").GetInt32());
            formats.Add(body.RootElement.TryGetProperty("response_format", out _));
            return Reply(Completion("Plain answer", "stop"));
        });
        var orders = new List<string?>();
        using var cached = new OpenRouterTextModel(new StubHandler(async (message, ct) =>
        {
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            orders.Add(body.RootElement.GetProperty("provider").TryGetProperty("order", out var order) ? order[0].GetString() : null);
            return Reply(Completion("Plain answer", "stop"));
        }), "qwen/qwen3.8-flash", "none");
        await cached.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None);
        Expect.That(orders.Single() == "alibaba", "A cached model was not pinned to its caching provider");
        using var low = new OpenRouterTextModel(handler, "z-ai/glm-5.3-flash");
        using var none = new OpenRouterTextModel(handler, "deepseek/deepseek-v4.1-flash", "none");
        Expect.That(await low.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None) == "Plain answer" &&
            await none.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None) == "Plain answer",
            "A routine text reply was lost");
        Expect.That(caps.SequenceEqual([1536, 1024]) && formats.All(x => !x), "Routine tasks keep their own cap and send no note schema");
    }

    static async Task RejectedReplies()
    {
        var usage = new List<string>();
        await ExpectRejected(Reply(Completion("{\"sections\":[", "length")), "The model reached the output limit", usage);
        Expect.That(usage.Count == 1 && usage[0].Contains("prompt_tokens"), "A truncated paid reply lost its usage");
        await ExpectRejected(Reply("{\"error\":{\"code\":429,\"message\":\"quoted lecture text\"}}"), "error 429");
        await ExpectRejected(Reply(Completion("", "stop")), "no text");
        await ExpectRejected(Reply(Completion("Partial", "content_filter")), "content_filter");
        await ExpectRejected(Reply(new string('x', 2_000_001)), "exceeds limit");
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Found })
        {
            using var model = new OpenRouterTextModel(new StubHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent("quoted lecture text") })), "deepseek/deepseek-v4.1-flash");
            try
            {
                await model.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None);
                throw new CheckFailed("A failed HTTP status was accepted");
            }
            catch (HttpRequestException ex)
            {
                Expect.That(ex.Message == $"OpenRouter text HTTP {(int)status}", "HTTP failures and redirects must surface without the provider body");
            }
        }
    }

    static async Task ExpectRejected(HttpResponseMessage response, string reason, List<string>? usage = null)
    {
        using var model = new OpenRouterTextModel(new StubHandler((_, _) => Task.FromResult(response)), "deepseek/deepseek-v4.1-flash");
        try
        {
            await model.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, usage is null ? null : usage.Add);
            throw new CheckFailed($"A reply that should fail with '{reason}' was accepted");
        }
        catch (InvalidOperationException ex)
        {
            Expect.That(ex.Message.Contains(reason) && !ex.Message.Contains("quoted lecture text"), $"Unexpected rejection: {ex.Message}");
        }
    }

    static async Task ExpectNoCall(string expectation)
    {
        var calls = 0;
        using var model = new OpenRouterTextModel(new StubHandler((_, _) => { calls++; return Task.FromResult(Reply(Completion("x", "stop"))); }),
            "deepseek/deepseek-v4.1-flash");
        await Expect.RejectsAsync(() => model.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None), expectation);
        Expect.That(calls == 0, expectation);
    }

    static string Completion(string content, string finish) => JsonSerializer.Serialize(new
    {
        provider = "DeepInfra",
        choices = new[] { new { finish_reason = finish, message = new { role = "assistant", content } } },
        usage = new { prompt_tokens = 120, completion_tokens = 40, cost = 0.00002 }
    });

    static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
