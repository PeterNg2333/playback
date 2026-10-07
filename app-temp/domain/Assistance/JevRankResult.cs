using System.Text.Json;
namespace Playback.Api.Terms;

public sealed record JevRankResult(
    string Term, bool RuleSuggestExplanation, string Model, long LatencyMs,
    double JevProbability, string JevRank, double JevConfidence, JsonElement Usage, bool Cached);
