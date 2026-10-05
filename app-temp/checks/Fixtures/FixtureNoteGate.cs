using System.Collections.Concurrent;
using Playback.Api.Notes;
using Playback.Api.Providers;

// A Jev note gate that answers allow or wait as told, records every input, and can fail or block one
// session on purpose to show that a slow session does not hold up the others.
sealed class FixtureNoteGate() : JevNoteGate(new JevTransport(new NoteGateHandler()))
{
    public override bool IsConfigured => true;
    public int Calls;
    public bool Allow = true, FailNext;
    public string? BlockSession;
    public readonly ConcurrentBag<string> Inputs = new();
    public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task<NoteGateDecision> Decide(string state, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        Inputs.Add(state);
        if (FailNext)
        {
            FailNext = false;
            throw new TimeoutException("Synthetic provider timeout");
        }
        if (BlockSession is not null && state.Contains("slow session"))
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
        return new(Allow, Allow ? .92 : .1, .9, "fixture-gate", "{\"input_tokens\":12}", 1);
    }
}
