using System.Net;
using System.Text.Json;

// In-memory Jev (TypeSafe) endpoints: model discovery plus the note-gate or term-ranking answer.

// Answers the note gate's "update now or wait" questions.
sealed class NoteGateHandler : HttpMessageHandler
{
    public int Models;
    public string? Request;
    public double Confidence = .9;
    public bool Invalid;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Get)
        {
            Models++;
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[{\"name\":\"fixture-alias\"}]}") };
        }
        Request = await request.Content!.ReadAsStringAsync(ct);
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = "fixture-version",
                answers = new
                {
                    update = new { type = "noul", noul = Invalid ? 2 : .95 },
                    action = new { type = "choice", choice = "update", confidence = Confidence, probabilities = new { update = .95, wait = .05 } }
                },
                usage = new { input_tokens = 12, output_tokens = 3 }
            }))
        };
    }
}

// Ranks every term "high" and counts discovery and ranking calls, to show the cache saves paid calls.
sealed class TermRankHandler : HttpMessageHandler
{
    public int ModelRequests;
    public int RankRequests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == "/v1/models")
        {
            Interlocked.Increment(ref ModelRequests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[{\"name\":\"demo-jev\"}]}")
            });
        }
        if (request.Method == HttpMethod.Post && path == "/v1/systemone")
        {
            Interlocked.Increment(ref RankRequests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"answers\":{\"explain\":{\"noul\":0.92},\"category\":{\"choice\":\"high\",\"confidence\":0.87}},\"usage\":{\"input_tokens\":5}}")
            });
        }
        throw new InvalidOperationException("Unexpected Jev fixture request");
    }
}

sealed class UnauthorizedJevHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
}
