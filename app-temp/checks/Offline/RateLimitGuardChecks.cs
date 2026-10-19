using System.Net;

// The live-run rate-limit guard (Validation/RateLimitGuard.cs), with an in-memory provider: only the first ten writer
// calls decide whether a run stops (three 429s), a later 429 is logged without stopping, organizer calls are not
// counted, and the log keeps the provider's reason but not the account identifier.
static class RateLimitGuardChecks
{
    public static async Task Run()
    {
        await Stops(); await LaterLimitsOnlyLogged();
        Console.WriteLine("Rate-limit guard checks passed (in-memory HTTP): three 429s in the first ten writer calls stop a run, later ones and organizer calls do not, account identifier not logged");
    }

    static async Task Stops()
    {
        var provider = new Provider(call => call is 1 or 4 or 9);
        var log = Path.GetTempFileName();
        using var client = new HttpClient(new RateLimitGuard(provider, log));
        for (var i = 0; i < 9; i++) await Send(client, "writer");
        Expect.That(provider.Requests == 9, "The guard sent too few requests before deciding");
        var refused = false;
        try { await Send(client, "writer"); } catch (HttpRequestException) { refused = true; }
        Expect.That(refused, "A run kept calling after three 429s in its first ten writer calls");
        Expect.That(provider.Requests == 9, "A stopped run still reached the provider");
        var lines = File.ReadAllLines(log);
        Expect.That(lines.Length == 3 && lines.All(x => x.Contains("limit_burst_rate") && !x.Contains("user_abc")) && lines[^1].Contains("\"stoppedRun\":true"),
            "The 429 log lost the provider's reason, kept the account identifier, or did not mark the stop");
        File.Delete(log);
    }

    static async Task LaterLimitsOnlyLogged()
    {
        // Requests 1–2 are organizer calls (2 gets 429); writer call n is request n + 2. One 429 in the first ten
        // writer calls (request 5), three after them (requests 13–15).
        var provider = new Provider(call => call is 1 + 1 or 5 or 13 or 14 or 15);
        using var client = new HttpClient(new RateLimitGuard(provider, null));
        await Send(client, "Reorganize the selected scope");
        await Send(client, "Reorganize the selected scope");
        for (var i = 0; i < 14; i++) await Send(client, "writer");
        Expect.That(provider.Requests == 16, "429s after the tenth writer call, or organizer 429s, stopped the run");
    }

    static Task<HttpResponseMessage> Send(HttpClient client, string body) => client.PostAsync("https://openrouter.test/", new StringContent(body));

    // Answers 429 to the calls the rule picks (1-based over every request).
    sealed class Provider(Func<int, bool> limited) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(limited(++Requests)
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"error\":{\"code\":429,\"metadata\":{\"provider_error_code\":\"limit_burst_rate\"}},\"user_id\":\"user_abc\"}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}
