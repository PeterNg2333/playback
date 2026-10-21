using System.Net;
using System.Text;
using System.Text.Json;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Application.Notes;

// OpenRouter text models: the fixed endpoint and process key, each note task's schema and output cap, routing
// that requires every parameter and skips 4-bit quantizations, usage kept on rejected replies, and errors that
// never echo provider text. Streaming: the draft grows while server-sent events arrive, usage comes from the last
// chunk, and a mid-stream error, dropped connection, silence or deadline fails as a transient error that the note
// writer may retry. Every HTTP call is answered in memory.
static class OpenRouterTextChecks
{
    const string NoteInput = "{\"window\":[[\"T1\",\"0:00\",\"mic\",\"TCP uses a three-way handshake.\"]]}";
    const string NoteReply = "{\"title\":\"TCP\",\"blocks\":[{\"kind\":\"concept\",\"markdown\":\"TCP 用三次握手建立連線。 [T1]\",\"sources\":[\"T1\"]}],\"concepts\":[],\"corrections\":[]}";
    static readonly NoteTask SegmentTask = NotePrompts.Segment;
    static readonly OpenRouterTextModel.Limits Quick = new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300));

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
            await StreamedDraft();
            await StreamFailures();
            await StreamTimeouts();
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
        Console.WriteLine("OpenRouter text checks passed (in-memory HTTP): fixed endpoint and key, note schema and caps, parameter-complete fp8+ routing, " +
            "usage on rejection, safe errors, offline guard; SSE draft before the reply ends, usage from the last chunk, mid-stream error, " +
            "dropped connection, silence and deadline as transient failures, caller cancellation, stream size limit");
    }

    static async Task NoteRequest()
    {
        var handler = new OpenRouterStub(async (message, ct) =>
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
            Expect.That(root.GetProperty("max_tokens").GetInt32() == SegmentTask.MaxOutputTokens * 3 / 2, "Note replies keep their task cap plus bounded reasoning room");
            Expect.That(root.GetProperty("reasoning").GetProperty("effort").GetString() == "low" &&
                root.GetProperty("reasoning").GetProperty("exclude").GetBoolean(), "Reasoning effort must be explicit and its text not returned");
            Expect.That(root.GetProperty("stream").GetBoolean(), "A note writer with a live draft must ask for a stream");
            var provider = root.GetProperty("provider");
            var quantizations = provider.GetProperty("quantizations").EnumerateArray().Select(x => x.GetString()).ToList();
            Expect.That(provider.GetProperty("require_parameters").GetBoolean() && quantizations.Contains("fp8") &&
                !quantizations.Any(x => x is "fp4" or "int4" or "mxfp4" or "nvfp4"), "Routing must require every parameter and skip 4-bit quantizations");
            var format = root.GetProperty("response_format");
            Expect.That(format.GetProperty("type").GetString() == "json_schema" && format.GetProperty("json_schema").GetProperty("strict").GetBoolean() &&
                format.GetProperty("json_schema").GetProperty("name").GetString() == SegmentTask.Name &&
                format.GetProperty("json_schema").GetProperty("schema").GetRawText() == JsonDocument.Parse(SegmentTask.Schema).RootElement.GetRawText(),
                "A note call must send its task schema as a strict JSON schema");
            return Reply(Completion(NoteReply, "stop"));
        });
        var usage = new List<string>();
        using var model = new OpenRouterTextModel(handler, "deepseek/deepseek-v4.1-flash");
        // A provider may answer a streamed request with one JSON body; it is read the same way.
        var reply = await new OpenRouterNoteWriter(model).Complete(SegmentTask, "Write notes.", NoteInput, CancellationToken.None, onUsage: usage.Add);
        Expect.That(reply == NoteReply, "A structured note reply was not returned as written");
        Expect.That(usage.Single().Contains("\"provider\":\"DeepInfra\"") && usage.Single().Contains("\"cost\""),
            "Usage must keep the serving provider and reported cost");
    }

    static async Task RoutineRequests()
    {
        var caps = new List<int>();
        var formats = new List<bool>();
        var streams = new List<bool>();
        var handler = new OpenRouterStub(async (message, ct) =>
        {
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            caps.Add(body.RootElement.GetProperty("max_tokens").GetInt32());
            formats.Add(body.RootElement.TryGetProperty("response_format", out _));
            streams.Add(body.RootElement.TryGetProperty("stream", out _));
            return Reply(Completion("Plain answer", "stop"));
        });
        var orders = new List<string?>();
        using var cached = new OpenRouterTextModel(new OpenRouterStub(async (message, ct) =>
        {
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            orders.Add(body.RootElement.GetProperty("provider").TryGetProperty("order", out var order) ? order[0].GetString() : null);
            return Reply(Completion("Plain answer", "stop"));
        }), "qwen/qwen3.8-flash", "none");
        await cached.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None);
        Expect.That(orders.Single() == "alibaba", "A cached model was not pinned to its caching provider");

        // Provider routing: Alibaba is preferred, other providers stay allowed (allow_fallbacks left at its default true),
        // and each request names one model with its own reasoning setting (no "models" list; see NoteWriterFallback.cs).
        using var routed = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(Reply(Completion("x", "stop")))), "qwen/qwen3.8-flash", "none");
        var payload = routed.Payload("LectureSectionWriter", "Write notes.", NoteInput);
        Expect.That(payload["model"]!.GetValue<string>() == "qwen/qwen3.8-flash" && !payload.ContainsKey("models") && !payload.ContainsKey("usage") &&
            payload["provider"]!["order"]![0]!.GetValue<string>() == "alibaba" && payload["provider"]!["allow_fallbacks"] is null &&
            payload["provider"]!["require_parameters"]!.GetValue<bool>(), "The note request's provider routing changed");
        using var low = new OpenRouterTextModel(handler, "z-ai/glm-5.3-flash");
        using var none = new OpenRouterTextModel(handler, "deepseek/deepseek-v4.1-flash", "none");
        Expect.That(await low.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None) == "Plain answer" &&
            await none.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None) == "Plain answer",
            "A routine text reply was lost");
        Expect.That(caps.SequenceEqual([1536, 1024]) && formats.All(x => !x) && streams.All(x => !x),
            "Routine tasks keep their own cap, send no note schema and are not streamed without a draft");
    }

    static async Task RejectedReplies()
    {
        var usage = new List<string>();
        await ExpectRejected(Reply(Completion("{\"sections\":[", "length")), "The model reached the output limit", usage);
        Expect.That(usage.Count == 1 && usage[0].Contains("prompt_tokens"), "A truncated paid reply lost its usage");
        await ExpectRejected(Reply("{\"error\":{\"code\":400,\"message\":\"quoted lecture text\"}}"), "error 400");
        await ExpectRejected(Reply(Completion("", "stop")), "no text");
        await ExpectRejected(Reply(Completion("Partial", "content_filter")), "content_filter");
        await ExpectRejected(Reply(new string('x', 2_000_001)), "exceeds limit");
        // A provider error reported inside a 200 body is transient when its code is.
        var transient = await Failure(Reply("{\"error\":{\"code\":429,\"message\":\"quoted lecture text\"}}"));
        Expect.That(transient is OpenRouterTransientException && transient.Message.Contains("error 429") && !transient.Message.Contains("quoted"),
            "A rate-limited reply inside a 200 body was not a transient failure");
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Found, HttpStatusCode.BadRequest, HttpStatusCode.PaymentRequired })
        {
            var error = await Failure(new HttpResponseMessage(status) { Content = new StringContent("quoted lecture text") });
            Expect.That(error is HttpRequestException and not OpenRouterTransientException && error.Message == $"OpenRouter text HTTP {(int)status}" &&
                !OpenRouterTextModel.IsTransient(error), "HTTP failures and redirects must surface without the provider body, and not be retried");
        }
        foreach (var status in new[] { HttpStatusCode.RequestTimeout, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable })
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("quoted lecture text") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            var error = await Failure(response);
            Expect.That(error is OpenRouterTransientException { RetryAfter.TotalSeconds: 7 } && error.Message == $"OpenRouter text HTTP {(int)status}",
                "HTTP 408, 429 and 5xx must be transient and keep the provider's Retry-After");
        }
    }

    // The reply arrives in pieces; the draft shows the first piece while the provider is still sending, comments
    // are ignored, a piece split inside a UTF-8 character or a line still decodes, and usage comes from the last chunk.
    static async Task StreamedDraft()
    {
        var body = new SseBody();
        var drafts = new List<string>();
        var usage = new List<string>();
        var firstDraft = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(body.Response())), "qwen/qwen3.8-flash", "none", Quick);
        var reply = model.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, usage.Add, text =>
        {
            lock (drafts) drafts.Add(text);
            firstDraft.TrySetResult();
        });
        var half = NoteReply.IndexOf("三次", StringComparison.Ordinal);
        body.Comment();
        body.Event(OpenRouterStub.Delta(NoteReply[..half]));
        await firstDraft.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Expect.That(!reply.IsCompleted && drafts.Single() == NoteReply[..half], "The draft did not show the first streamed piece before the reply ended");
        var rest = Encoding.UTF8.GetBytes("data: " + OpenRouterStub.Delta(NoteReply[half..]) + "\n\n");
        var split = Array.FindIndex(rest, x => x >= 0xE0) + 1;   // inside the first three-byte Chinese character
        Expect.That(split > 0, "The streamed fixture has no multi-byte character to split");
        body.Raw(rest[..split]);
        body.Raw(rest[split..]);
        body.Comment();
        body.Event(OpenRouterStub.Delta("", "stop"));
        body.Event(OpenRouterStub.UsageChunk());
        body.Done();
        Expect.That(await reply == NoteReply && drafts[^1] == NoteReply, "The streamed reply or its final draft was not the whole reply");
        Expect.That(usage.Count == 1 && usage[0].Contains("\"cached_tokens\":1000") && usage[0].Contains("\"provider\":\"Alibaba\"") &&
            usage[0].Contains("\"model\":\"qwen/qwen3.8-flash\"") && usage[0].Contains("\"cost\""),
            "Usage, cached tokens, the serving provider and the answering model must come from the last chunk");
        using (var stats = JsonDocument.Parse(usage[0]))
        {
            var stream = stats.RootElement.GetProperty("stream");
            Expect.That(stream.GetProperty("keepAlives").GetInt32() == 2 && stream.GetProperty("firstContentMs").GetInt64() <= stream.GetProperty("totalMs").GetInt64() &&
                stream.GetProperty("longestGapMs").GetInt64() >= 0, "The usage report did not describe the stream (keep-alives, first content, longest gap)");
        }

        // A note writer streams for its draft and still parses the finished reply.
        var writerDrafts = new List<string>();
        using var writerModel = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(OpenRouterStub.Stream([NoteReply[..10], NoteReply[10..]]))),
            "qwen/qwen3.8-flash", "none", Quick);
        var streamed = await new OpenRouterNoteWriter(writerModel).Complete(SegmentTask, "Write notes.", NoteInput, CancellationToken.None, writerDrafts.Add);
        Expect.That(streamed == NoteReply && writerDrafts.Count >= 1 && writerDrafts[^1] == NoteReply,
            "A streamed note reply was not parsed or its draft was lost");
    }

    static async Task StreamFailures()
    {
        // The provider fails after the stream began: HTTP 200 with an error event and finish_reason "error".
        var usage = new List<string>();
        var body = new SseBody();
        body.Event(OpenRouterStub.Delta("{\"outline\":["));
        body.Event(OpenRouterStub.ErrorChunk("server_error"));
        body.End();
        var error = await StreamFailure(body, usage);
        Expect.That(error is OpenRouterTransientException && error.Message.Contains("server_error") && !error.Message.Contains("quoted"),
            "A mid-stream provider error must be transient and must not quote the provider message");

        // The connection closes before the reply finished: no finish reason, no [DONE].
        body = new SseBody();
        body.Event(OpenRouterStub.Delta("{\"outline\":["));
        body.End();
        Expect.That(await StreamFailure(body) is OpenRouterTransientException { Message: var ended } && ended.Contains("ended before"),
            "A stream that ended early was accepted or not marked transient");

        // The connection is reset while reading.
        body = new SseBody();
        body.Event(OpenRouterStub.Delta("{\"outline\":["));
        body.Drop();
        Expect.That(await StreamFailure(body) is OpenRouterTransientException { Message: var dropped } && dropped.Contains("dropped"),
            "A reset connection was not a transient failure");

        // Output limit inside a stream: rejected, not transient, usage kept.
        usage.Clear();
        body = new SseBody();
        body.Event(OpenRouterStub.Delta("{\"outline\":["));
        body.Event(OpenRouterStub.Delta("", "length"));
        body.Event(OpenRouterStub.UsageChunk("length"));
        body.Done();
        error = await StreamFailure(body, usage);
        Expect.That(error is InvalidOperationException && error.Message.Contains("output limit") && !OpenRouterTextModel.IsTransient(error) && usage.Count == 1,
            "A streamed reply cut at the output limit must be rejected with its usage and not retried");

        // A stream larger than the response limit stops reading.
        body = new SseBody();
        var piece = OpenRouterStub.Delta(new string('x', 100_000));
        for (var i = 0; i < 25; i++) body.Event(piece);
        body.Done();
        error = await StreamFailure(body);
        Expect.That(error is InvalidOperationException && error.Message.Contains("exceeds limit"), "A stream over the response limit was read to the end");
    }

    static async Task StreamTimeouts()
    {
        // Keep-alive comments are traffic: a quiet provider that keeps commenting is not a stall.
        var body = new SseBody();
        using var patient = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(body.Response())), "qwen/qwen3.8-flash", "none", Quick);
        var reply = patient.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, onUpdate: _ => { });
        for (var i = 0; i < 6; i++) { body.Comment(); await Task.Delay(100); }
        body.Event(OpenRouterStub.Delta(NoteReply, "stop"));
        body.Done();
        Expect.That(await reply == NoteReply, "Keep-alive comments did not keep a quiet stream open");

        // Silence longer than the limit is a stall.
        body = new SseBody();
        body.Event(OpenRouterStub.Delta("{\"outline\":["));
        var error = await StreamFailure(body);
        Expect.That(error is TimeoutException { Message: var silent } && silent.Contains("silent") && OpenRouterTextModel.IsTransient(error),
            "A silent stream did not time out as a transient stall");

        // Comments forever cannot outlast the call deadline.
        body = new SseBody();
        using var bounded = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(body.Response())), "qwen/qwen3.8-flash", "none",
            new OpenRouterTextModel.Limits(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(300)));
        using var keepAlive = new CancellationTokenSource();
        var commenting = Task.Run(async () => { while (!keepAlive.IsCancellationRequested) { body.Comment(); await Task.Delay(50); } });
        try
        {
            await bounded.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, onUpdate: _ => { });
            throw new CheckFailed("A stream that never finished outlived the call deadline");
        }
        catch (TimeoutException ex) { Expect.That(ex.Message.Contains("exceeded"), "The call deadline was reported as something else"); }
        finally { keepAlive.Cancel(); await commenting; }

        // The caller's own cancellation is a cancellation, never a timeout to retry.
        body = new SseBody();
        using var cancel = new CancellationTokenSource();
        using var model = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(body.Response())), "qwen/qwen3.8-flash", "none", Quick);
        var cancelled = model.Generate("LectureSectionWriter", "Write notes.", NoteInput, cancel.Token, onUpdate: _ => cancel.Cancel());
        body.Event(OpenRouterStub.Delta("{\"outline\":["));
        try { await cancelled; throw new CheckFailed("A cancelled stream completed"); }
        catch (OperationCanceledException) { }
    }

    static async Task<Exception> StreamFailure(SseBody body, List<string>? usage = null)
    {
        using var model = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(body.Response())), "qwen/qwen3.8-flash", "none", Quick);
        try
        {
            await model.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, usage is null ? null : usage.Add, _ => { });
        }
        catch (Exception ex) when (ex is not CheckFailed) { return ex; }
        throw new CheckFailed("A failing stream was accepted");
    }

    static async Task<Exception> Failure(HttpResponseMessage response)
    {
        using var model = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(response)), "deepseek/deepseek-v4.1-flash");
        try { await model.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None); }
        catch (Exception ex) when (ex is not CheckFailed) { return ex; }
        throw new CheckFailed("A failed reply was accepted");
    }

    static async Task ExpectRejected(HttpResponseMessage response, string reason, List<string>? usage = null)
    {
        using var model = new OpenRouterTextModel(new OpenRouterStub((_, _) => Task.FromResult(response)), "deepseek/deepseek-v4.1-flash");
        try
        {
            await model.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, usage is null ? null : usage.Add);
            throw new CheckFailed($"A reply that should fail with '{reason}' was accepted");
        }
        catch (InvalidOperationException ex)
        {
            Expect.That(ex.Message.Contains(reason) && !ex.Message.Contains("quoted lecture text") && !OpenRouterTextModel.IsTransient(ex),
                $"Unexpected rejection: {ex.Message}");
        }
    }

    static async Task ExpectNoCall(string expectation)
    {
        var handler = new OpenRouterStub((_, _) => Task.FromResult(Reply(Completion("x", "stop"))));
        using var model = new OpenRouterTextModel(handler, "deepseek/deepseek-v4.1-flash");
        await Expect.RejectsAsync(() => model.Generate("PlaybackQuestionAnswerer", "Answer.", "Question", CancellationToken.None), expectation);
        Expect.That(handler.Calls == 0, expectation);
    }

    static string Completion(string content, string finish) => JsonSerializer.Serialize(new
    {
        provider = "DeepInfra",
        choices = new[] { new { finish_reason = finish, message = new { role = "assistant", content } } },
        usage = new { prompt_tokens = 120, completion_tokens = 40, cost = 0.00002 }
    });

    static HttpResponseMessage Reply(string body) => OpenRouterStub.Json(body);
}
