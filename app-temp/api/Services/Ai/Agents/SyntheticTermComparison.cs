using System.Diagnostics;
using System.Text.Json;
using Playback.Api.Services.Ai.Providers;

namespace Playback.Api.Services.Ai.Agents;

public sealed class SyntheticTermComparison(JevTermClassifier jev, GeminiLanguageModel gemini)
{
    public async Task<object> Evaluate(CancellationToken ct)
    {
        var examples = new[]
        {
            (term: "phoneme", expected: true),
            (term: "the", expected: false),
            (term: "spectrogram", expected: true),
            (term: "today", expected: false)
        };
        var results = new List<object>();
        foreach (var example in examples)
        {
            var timer = Stopwatch.StartNew();
            var rule = example.term.Length >= 8 || example.term.Any(char.IsUpper);
            var ruleMs = timer.ElapsedMilliseconds;
            object? jevResult = null;
            object? geminiResult = null;
            if (jev.IsConfigured)
            {
                try
                {
                    jevResult = await jev.Rank(example.term, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
                {
                    jevResult = new { error = ex.Message };
                }
            }
            if (gemini.IsConfigured)
            {
                timer.Restart();
                try
                {
                    var response = await gemini.Generate(
                        "SyntheticTermClassifier",
                        "Return only YES or NO: Would a concise explanation of this academic term help a general class audience?",
                        example.term, ct, "gemini-3.5-flash-lite");
                    geminiResult = new
                    {
                        response,
                        latencyMs = timer.ElapsedMilliseconds,
                        usage = "not exposed by this agent call"
                    };
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    geminiResult = new { error = ex.Message };
                }
            }
            results.Add(new
            {
                example.term,
                example.expected,
                rule = new { suggest = rule, latencyMs = ruleMs },
                jev = jevResult ?? new { unavailable = "JEV_API_KEY is absent" },
                gemini = geminiResult ?? new { unavailable = "GOOGLE_AI_STUDIO_API_KEY is absent" }
            });
        }
        return new { dataset = "Four labeled synthetic terms; no lecture content", results };
    }
}
