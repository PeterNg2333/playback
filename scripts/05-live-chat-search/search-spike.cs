#:property NuGetAudit=false
#:property PublishAot=false

// This file uses only the .NET shared framework. Disable restore-only network work so Demo stays offline;
// Wikipedia mode still performs the explicit HTTP request below.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.OutputEncoding = Encoding.UTF8;

var options = SearchOptions.Parse(args);
ValidateQuery(options.Query);
if (options.SelfTest && options.Mode != "demo")
{
    throw new ArgumentException("--self-test is deterministic and can only run in demo mode.");
}

string json;
if (options.Mode == "demo")
{
    json = await ReadBoundedTextAsync(options.SamplePath!, 100_000);
}
else if (options.Mode == "wikipedia")
{
    var requestUri = BuildWikipediaUri(options.Query);
    Console.WriteLine("GET " + requestUri.GetLeftPart(UriPartial.Path) + "?…");

    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AsrLlmNoteSpike", "0.1"));
    using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    using var response = await client.GetAsync(
        requestUri,
        HttpCompletionOption.ResponseHeadersRead,
        requestTimeout.Token);
    response.EnsureSuccessStatusCode();
    json = await ReadBoundedUtf8Async(response.Content, 512_000, requestTimeout.Token);
}
else
{
    throw new ArgumentException("Mode must be demo or wikipedia.");
}

var evidence = ParseEvidence(json);

if (options.SelfTest)
{
    Expect(evidence.Count == 3, "the fixture produces exactly three evidence items");
    Expect(evidence.All(item => item.Url.Scheme == Uri.UriSchemeHttps), "all citations use HTTPS");
    Expect(evidence.All(item => item.Url.Host == "en.wikipedia.org"), "all citations use the allowlisted host");
    Expect(BuildWikipediaUri("VAD & ASR").Host == "en.wikipedia.org", "search host is fixed, not user-controlled");

    const string hostileCitation = """
    {"query":{"pages":[{"title":"Looks plausible","extract":"But the URL is not Wikipedia.","fullurl":"https://attacker.example/claim"}]}}
    """;
    Expect(ParseEvidence(hostileCitation).Count == 0, "an off-domain citation is rejected");

    using var oversized = new ByteArrayContent(new byte[33]);
    await ExpectThrowsAsync(
        () => ReadBoundedUtf8Async(oversized, 32, CancellationToken.None),
        "an oversized response body is rejected");
    Console.WriteLine("\nSELF-TEST PASS");
}

if (evidence.Count == 0)
{
    Console.WriteLine("No evidence found. A real chat must say that it lacks sources.");
    return;
}

Console.WriteLine();
Console.WriteLine("# Evidence pack (not yet an LLM answer)");
for (var index = 0; index < evidence.Count; index++)
{
    var item = evidence[index];
    Console.WriteLine($"\n[{index + 1}] {item.Title}\n{item.Extract}\nSource: {item.Url}");
}

static Uri BuildWikipediaUri(string query)
{
    const string endpoint = "https://en.wikipedia.org/w/api.php";
    var encoded = Uri.EscapeDataString(query);
    var queryString = "action=query&generator=search&gsrsearch=" + encoded
        + "&gsrlimit=3&prop=extracts%7Cinfo&exintro=1&explaintext=1&inprop=url&format=json&formatversion=2";
    return new Uri(endpoint + "?" + queryString, UriKind.Absolute);
}

static List<EvidenceItem> ParseEvidence(string json)
{
    var payload = JsonSerializer.Deserialize<WikipediaResponse>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    return (payload?.Query?.Pages ?? [])
        .Where(page => !string.IsNullOrWhiteSpace(page.Title)
            && !string.IsNullOrWhiteSpace(page.Extract)
            && Uri.TryCreate(page.FullUrl, UriKind.Absolute, out var uri)
            && IsAllowedCitation(uri))
        .Take(3)
        .Select(page => new EvidenceItem(
            SanitizePlainText(page.Title!),
            SanitizePlainText(page.Extract!),
            new Uri(page.FullUrl!, UriKind.Absolute)))
        .ToList();
}

static bool IsAllowedCitation(Uri uri) =>
    uri.Scheme == Uri.UriSchemeHttps
    && uri.Host.Equals("en.wikipedia.org", StringComparison.OrdinalIgnoreCase)
    && uri.IsDefaultPort
    && string.IsNullOrEmpty(uri.UserInfo);

static string SanitizePlainText(string value)
{
    var withoutControls = new string(value
        .Where(character => !char.IsControl(character) || char.IsWhiteSpace(character))
        .ToArray());
    return string.Join(' ', withoutControls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

static void ValidateQuery(string query)
{
    if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query cannot be empty.");
    if (query.Length > 200) throw new ArgumentException("Query exceeds the 200-character spike limit.");
}

static async Task<string> ReadBoundedTextAsync(string path, long maximumBytes)
{
    var file = new FileInfo(Path.GetFullPath(path));
    if (!file.Exists) throw new FileNotFoundException("Sample response not found.", file.FullName);
    if (file.Length > maximumBytes) throw new InvalidOperationException("Sample response is unexpectedly large.");
    return await File.ReadAllTextAsync(file.FullName, Encoding.UTF8);
}

static async Task<string> ReadBoundedUtf8Async(
    HttpContent content,
    int maximumBytes,
    CancellationToken cancellationToken)
{
    if (content.Headers.ContentLength is long declaredLength && declaredLength > maximumBytes)
    {
        throw new InvalidOperationException($"Search response exceeds the {maximumBytes:N0}-byte limit.");
    }

    await using var input = await content.ReadAsStreamAsync(cancellationToken);
    using var output = new MemoryStream();
    var buffer = new byte[8_192];
    while (true)
    {
        var bytesRead = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
        if (bytesRead == 0) break;
        if (output.Length + bytesRead > maximumBytes)
        {
            throw new InvalidOperationException($"Search response exceeds the {maximumBytes:N0}-byte limit.");
        }
        output.Write(buffer, 0, bytesRead);
    }
    return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Self-test failed: " + message);
}

static async Task ExpectThrowsAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException("Self-test failed: " + message);
}

internal sealed record SearchOptions(string Mode, string Query, string? SamplePath, bool SelfTest)
{
    public static SearchOptions Parse(string[] values)
    {
        string mode = "demo";
        string query = "voice activity detection";
        string? sample = null;
        var selfTest = false;

        for (var index = 0; index < values.Length; index++)
        {
            switch (values[index])
            {
                case "--self-test": selfTest = true; break;
                case "--mode": mode = ReadValue(values, ref index); break;
                case "--query": query = ReadValue(values, ref index); break;
                case "--sample": sample = ReadValue(values, ref index); break;
                default: throw new ArgumentException($"Unknown argument: {values[index]}");
            }
        }

        if (mode == "demo" && string.IsNullOrWhiteSpace(sample))
        {
            throw new ArgumentException("--sample is required in demo mode.");
        }
        return new SearchOptions(mode, query.Trim(), sample, selfTest);
    }

    private static string ReadValue(string[] values, ref int index)
    {
        if (++index >= values.Length) throw new ArgumentException("An option is missing its value.");
        return values[index];
    }
}

internal sealed record EvidenceItem(string Title, string Extract, Uri Url);

internal sealed class WikipediaResponse
{
    [JsonPropertyName("query")]
    public WikipediaQuery? Query { get; init; }
}

internal sealed class WikipediaQuery
{
    [JsonPropertyName("pages")]
    public List<WikipediaPage>? Pages { get; init; }
}

internal sealed class WikipediaPage
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("extract")]
    public string? Extract { get; init; }

    [JsonPropertyName("fullurl")]
    public string? FullUrl { get; init; }
}
