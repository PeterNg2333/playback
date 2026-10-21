using System.Text.Json;
namespace Playback.Domain.Assistance;

public sealed record WebEvidence(string Kind, string Url, string Title, int StartIndex, int EndIndex);
public sealed record GroundedResult(string Answer, List<WebEvidence> Evidence, bool Inference, string SearchSuggestions,
    List<string>? SearchQueries = null, string? UsageJson = null, string? GroundingMetadataJson = null);
