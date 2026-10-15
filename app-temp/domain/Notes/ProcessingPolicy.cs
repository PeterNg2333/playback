namespace Playback.Domain.Notes;

// Missing fields inherit. An explicit false or zero remains an override.
public sealed record ProcessingOverrides(
    bool? JevEnabled = null,
    double? ProbabilityThreshold = null,
    double? ConfidenceThreshold = null,
    double? CheckIntervalSeconds = null,
    double? MaxPendingWaitSeconds = null,
    double? SilenceBoundarySeconds = null,
    double? BoundarySettleSeconds = null,
    bool? CombineQuiet = null,
    double? MaxCombinedSeconds = null,
    bool? OrganizationEnabled = null,
    int? OrganizationChangedRevisions = null,
    double? OrganizationMaxDirtyAgeSeconds = null,
    double? OrganizationCooldownSeconds = null,
    string? OrganizationScope = null,
    string? NotePresentation = null);

public sealed record ProcessingPolicy(
    bool JevEnabled = true,
    double ProbabilityThreshold = .70,
    double ConfidenceThreshold = .50,
    double CheckIntervalSeconds = 10,
    double MaxPendingWaitSeconds = 30,
    double SilenceBoundarySeconds = 2,
    double BoundarySettleSeconds = 2,
    bool CombineQuiet = true,
    double MaxCombinedSeconds = 60,
    bool OrganizationEnabled = true,
    int OrganizationChangedRevisions = 3,
    double OrganizationMaxDirtyAgeSeconds = 120,
    double OrganizationCooldownSeconds = 60,
    string OrganizationScope = "affected-topics",
    string NotePresentation = "numbered-hierarchy")
{
    public const int SchemaVersion = 1;
    public static ProcessingPolicy Defaults { get; } = new();
    public ProcessingPolicy Apply(ProcessingOverrides value)
    {
        var result = new ProcessingPolicy(value.JevEnabled ?? JevEnabled,
            value.ProbabilityThreshold ?? ProbabilityThreshold, value.ConfidenceThreshold ?? ConfidenceThreshold,
            value.CheckIntervalSeconds ?? CheckIntervalSeconds, value.MaxPendingWaitSeconds ?? MaxPendingWaitSeconds,
            value.SilenceBoundarySeconds ?? SilenceBoundarySeconds, value.BoundarySettleSeconds ?? BoundarySettleSeconds,
            value.CombineQuiet ?? CombineQuiet, value.MaxCombinedSeconds ?? MaxCombinedSeconds,
            value.OrganizationEnabled ?? OrganizationEnabled, value.OrganizationChangedRevisions ?? OrganizationChangedRevisions,
            value.OrganizationMaxDirtyAgeSeconds ?? OrganizationMaxDirtyAgeSeconds, value.OrganizationCooldownSeconds ?? OrganizationCooldownSeconds,
            value.OrganizationScope ?? OrganizationScope, value.NotePresentation ?? NotePresentation);
        result.Validate();
        return result;
    }
    public void Validate()
    {
        Range(ProbabilityThreshold, 0, 1, "Probability threshold");
        Range(ConfidenceThreshold, 0, 1, "Confidence threshold");
        Range(CheckIntervalSeconds, 2, 30, "Decision check interval (seconds)");
        Range(MaxPendingWaitSeconds, 10, 120, "Maximum pending wait (seconds)");
        Range(SilenceBoundarySeconds, .7, 10, "Silence boundary (seconds)");
        Range(BoundarySettleSeconds, 0, 5, "Boundary settle grace (seconds)");
        Range(MaxCombinedSeconds, 10, 300, "Maximum combined quiet duration (seconds)");
        Range(OrganizationChangedRevisions, 1, 10, "Changed revisions before organization");
        Range(OrganizationMaxDirtyAgeSeconds, 30, 600, "Maximum dirty-note age (seconds)");
        Range(OrganizationCooldownSeconds, 15, 300, "Organization cooldown (seconds)");
        if (OrganizationCooldownSeconds > OrganizationMaxDirtyAgeSeconds) throw new ProcessingPolicyValidationException("Organization cooldown must not exceed maximum dirty-note age");
        if (OrganizationScope is not ("affected-topics" or "document-outline")) throw new ProcessingPolicyValidationException("Invalid organization scope");
        if (NotePresentation is not ("numbered-hierarchy" or "headings")) throw new ProcessingPolicyValidationException("Invalid note presentation");
    }
    static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ProcessingPolicyValidationException($"{name} must be between {min} and {max}");
    }
}

public sealed class ProcessingPolicyValidationException(string message) : ArgumentException(message);
public sealed record EffectiveProcessingPolicy(ProcessingPolicy Values, string Revision);
