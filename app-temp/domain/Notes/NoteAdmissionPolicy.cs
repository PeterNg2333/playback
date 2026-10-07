namespace Playback.Domain.Notes;

public static class NoteAdmissionPolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(ProcessingPolicy.Defaults.CheckIntervalSeconds);
    static readonly TimeSpan RetryStep = TimeSpan.FromSeconds(30);

    public static bool AllowsDecision(string? action, double probability, double confidence, ProcessingPolicy? policy = null)
    {
        if (!double.IsFinite(probability) || probability is < 0 or > 1 ||
            !double.IsFinite(confidence) || confidence is < 0 or > 1 || action is not ("update" or "wait"))
            throw new InvalidOperationException("Jev returned an invalid note decision");
        policy ??= ProcessingPolicy.Defaults;
        return action == "update" && probability >= policy.ProbabilityThreshold && confidence >= policy.ConfidenceThreshold;
    }

    public static bool RetryBlocked(NoteGateRecord state, DateTime now) => state.Attempts >= 3 || state.RetryAt > now;
    public static DateTime RetryAt(int attempts, DateTime now) => now.Add(RetryStep * attempts);
    public static string? LocalAdmission(NoteGateRecord state, ProcessingPolicy policy, DateTime now, bool boundaryReady = false) =>
        state.FlushRequested ? "stop" :
        boundaryReady ? "speech-boundary" :
        state.FirstPendingAt is { } first && now >= first.AddSeconds(policy.MaxPendingWaitSeconds) ? "deadline" :
        !policy.JevEnabled ? "gate-disabled" : null;
}
