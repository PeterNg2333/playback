namespace Playback.Domain.Notes;

// Missing fields inherit. An explicit false or zero remains an override.
public sealed record ProcessingOverrides(
    bool? JevEnabled = null,
    double? ProbabilityThreshold = null,
    double? ConfidenceThreshold = null,
    double? CheckIntervalSeconds = null,
    double? MaxPendingWaitSeconds = null,
    double? SilenceBoundarySeconds = null,
    double? BoundarySettleSeconds = null);

public sealed record ProcessingPolicy(
    bool JevEnabled = true,
    double ProbabilityThreshold = .70,
    double ConfidenceThreshold = .50,
    double CheckIntervalSeconds = 10,
    double MaxPendingWaitSeconds = 30,
    double SilenceBoundarySeconds = 2,
    double BoundarySettleSeconds = 2)
{
    public const int SchemaVersion = 1;
    public static ProcessingPolicy Defaults { get; } = new();
    public ProcessingPolicy Apply(ProcessingOverrides value)
    {
        var result = new ProcessingPolicy(value.JevEnabled ?? JevEnabled,
            value.ProbabilityThreshold ?? ProbabilityThreshold, value.ConfidenceThreshold ?? ConfidenceThreshold,
            value.CheckIntervalSeconds ?? CheckIntervalSeconds, value.MaxPendingWaitSeconds ?? MaxPendingWaitSeconds,
            value.SilenceBoundarySeconds ?? SilenceBoundarySeconds, value.BoundarySettleSeconds ?? BoundarySettleSeconds);
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
    }
    static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ProcessingPolicyValidationException($"{name} must be between {min} and {max}");
    }
}

public sealed class ProcessingPolicyValidationException(string message) : ArgumentException(message);
public sealed record EffectiveProcessingPolicy(ProcessingPolicy Values, string Revision);
