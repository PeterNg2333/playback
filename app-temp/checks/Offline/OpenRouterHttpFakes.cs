using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

// In-memory OpenRouter: a handler that answers each request from a function, and a server-sent-event body the
// check writes into piece by piece, so a check can look at the draft while the reply is still arriving, drop the
// connection, or stay silent. Shapes follow https://openrouter.ai/docs/api-reference/streaming (read 2026-10-19).
sealed class OpenRouterStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return send(request, ct);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // A whole stream at once: each content piece becomes a delta chunk, then the finish, the usage chunk and [DONE].
    // model and provider: who answered, as OpenRouter reports it in every chunk (a fallback model when the asked one could not).
    public static HttpResponseMessage Stream(IEnumerable<string> pieces, string finish = "stop", string model = "qwen/qwen3.8-flash", string provider = "Alibaba")
    {
        var body = new SseBody();
        foreach (var piece in pieces) body.Event(Delta(piece, null, model, provider));
        body.Event(Delta("", finish, model, provider));
        body.Event(UsageChunk(finish, model, provider));
        body.Done();
        return body.Response();
    }

    // Chinese stays as UTF-8 bytes, so a network read can end inside a character.
    public static string Delta(string content, string? finish = null, string model = "qwen/qwen3.8-flash", string provider = "Alibaba") => JsonSerializer.Serialize(new
    {
        id = "gen-fixture", provider, model, @object = "chat.completion.chunk",
        choices = new[] { new { index = 0, delta = new { role = "assistant", content }, finish_reason = finish } }
    }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // OpenRouter repeats the finish reason on the usage chunk, with one content-free choice.
    public static string UsageChunk(string finish = "stop", string model = "qwen/qwen3.8-flash", string provider = "Alibaba") => JsonSerializer.Serialize(new
    {
        id = "gen-fixture", provider, model, @object = "chat.completion.chunk",
        choices = new[] { new { index = 0, delta = new { role = "assistant", content = "" }, finish_reason = finish } },
        usage = new { prompt_tokens = 1200, completion_tokens = 80, cost = 0.00011, prompt_tokens_details = new { cached_tokens = 1000 } }
    });

    public static string ErrorChunk(string code) => JsonSerializer.Serialize(new
    {
        id = "gen-fixture", provider = "Alibaba", @object = "chat.completion.chunk",
        error = new { code, message = "quoted lecture text" },
        choices = new[] { new { index = 0, delta = new { content = "" }, finish_reason = "error" } }
    });
}

// A response body the check writes raw SSE text into. Reads wait for the next write, honour cancellation like a
// network stream, and end or fail when the check says so.
sealed class SseBody
{
    readonly Channel<object> pieces = Channel.CreateUnbounded<object>();
    public void Write(string text) => pieces.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
    public void Raw(byte[] bytes) => pieces.Writer.TryWrite(bytes);
    public void Event(string json) => Write("data: " + json + "\n\n");
    public void Comment() => Write(": OPENROUTER PROCESSING\n\n");
    public void Done() { Write("data: [DONE]\n\n"); End(); }
    public void End() => pieces.Writer.TryComplete();
    public void Drop() => pieces.Writer.TryWrite(new IOException("connection reset by peer"));

    public HttpResponseMessage Response()
    {
        var content = new StreamContent(new Reader(pieces.Reader));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    sealed class Reader(ChannelReader<object> pieces) : Stream
    {
        byte[] current = [];
        int offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            while (offset == current.Length)
            {
                if (!await pieces.WaitToReadAsync(ct)) return 0;
                var next = await pieces.ReadAsync(ct);
                if (next is Exception error) throw error;
                (current, offset) = ((byte[])next, 0);
            }
            var count = Math.Min(buffer.Length, current.Length - offset);
            current.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
