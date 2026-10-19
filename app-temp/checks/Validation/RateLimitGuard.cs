using System.Net;
using System.Text;
using System.Text.Json;

// Opt-in guard for a paid live note-model run (PLAYBACK_NOTE_MODELS_STOP_ON_429=yes): each HTTP 429 the provider
// returns is written to PLAYBACK_RATE_LIMIT_LOG (status, Retry-After and rate-limit headers, the first 4 KB of the
// body; the key is only in the request and is never written). When 3 of the first 10 writer calls get 429, every
// later request fails here without reaching the network, so the run ends quickly instead of paying for a run that
// cannot measure anything. Organizer calls are not counted.
sealed class RateLimitGuard : DelegatingHandler
{
    const int Window = 10, Limit = 3, MaxBody = 4096;
    readonly string? log;
    int writerCalls, writerLimited;
    public bool Stopped { get; private set; }

    public RateLimitGuard() : this(new HttpClientHandler { AllowAutoRedirect = false }, Environment.GetEnvironmentVariable("PLAYBACK_RATE_LIMIT_LOG")) { }
    public RateLimitGuard(HttpMessageHandler inner, string? log) { InnerHandler = inner; this.log = log; }

    public static bool Enabled => Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_STOP_ON_429") == "yes";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Stopped) throw new HttpRequestException("Run stopped after repeated HTTP 429; no request sent", null, HttpStatusCode.TooManyRequests);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var writer = !body.Contains("Reorganize the selected scope", StringComparison.Ordinal);
        var number = writer ? Interlocked.Increment(ref writerCalls) : 0;
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;
        // Only the first Window writer calls decide; a 429 later in the run is logged but never stops it.
        if (writer && number <= Window && Interlocked.Increment(ref writerLimited) >= Limit) Stopped = true;
        var text = await response.Content.ReadAsStringAsync(ct);
        // The provider echoes an account identifier; it is not needed to diagnose a rate limit.
        var logged = System.Text.RegularExpressions.Regex.Replace(text, "\"user_id\"\\s*:\\s*\"[^\"]*\"", "\"user_id\":\"[removed]\"");
        string? Header(string name) => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
        var record = new
        {
            at = DateTime.UtcNow, call = writer ? "writer" : "organizer", writerCallNumber = writer ? number : (int?)null, status = (int)response.StatusCode,
            retryAfter = response.Headers.RetryAfter?.ToString(), rateLimitLimit = Header("X-RateLimit-Limit"), rateLimitRemaining = Header("X-RateLimit-Remaining"),
            rateLimitReset = Header("X-RateLimit-Reset"), body = logged.Length > MaxBody ? logged[..MaxBody] : logged, stoppedRun = Stopped
        };
        if (log is not null) await File.AppendAllTextAsync(log, JsonSerializer.Serialize(record) + "\n", Encoding.UTF8, ct);
        // The caller reads only the status; give it a fresh body since this one was consumed.
        response.Content = new StringContent(text, Encoding.UTF8, "application/json");
        return response;
    }
}
