using Playback.Domain.Notes;
using Playback.Api.Terms;
using Playback.Api.Sources;
using System.Text.RegularExpressions;

namespace Playback.Api.Db;

// The AI execution log behind the activity popover.
public sealed class ActivityRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Task { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "queued";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Summary { get; set; }
    public List<string> SourceIds { get; set; } = [];
    public int? BasedOnVersion { get; set; }
    public string? Draft { get; set; }
    public string? PromptVersion { get; set; }
    public string? PromptHash { get; set; }
    public string? InputHash { get; set; }
    public int? InputBytes { get; set; }
    public string? UsageJson { get; set; }
    public string? SectionId { get; set; }
    public long? ScheduleDelayMs { get; set; }
    public long? ProviderLatencyMs { get; set; }
    public long? QueueDelayMs { get; set; }
    public string? PromptText { get; set; }
}
