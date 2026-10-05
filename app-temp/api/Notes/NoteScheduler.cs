namespace Playback.Api.Notes;

// Scheduling uses monotonic TimeProvider timestamps and never awaits provider work on the tick.
// Admission is shared by automatic generation and section organization in NoteAgent.
public sealed class NoteScheduler(TimeProvider clock, Func<string, CancellationToken, Task> work, int concurrency = 4)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    readonly Dictionary<string, Task> active = new();
    long lastTick = clock.GetTimestamp();
    int next;
    public bool Due => clock.GetElapsedTime(lastTick) >= Interval;
    public IReadOnlyList<Task> Active { get { lock (active) return active.Values.ToList(); } }
    public void Tick(IEnumerable<string> sessionIds, CancellationToken ct) {
        lock (active) {
            if (clock.GetElapsedTime(lastTick) < Interval) return;
            lastTick = clock.GetTimestamp();
            foreach (var id in active.Where(x => x.Value.IsCompleted).Select(x => x.Key).ToList()) active.Remove(id);
            var ids = sessionIds.Distinct().Order(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return;
            var start = next % ids.Count;
            foreach (var offset in Enumerable.Range(0, ids.Count)) {
                var index = (start + offset) % ids.Count; var id = ids[index];
                if (active.Count >= concurrency) break;
                if (!active.ContainsKey(id)) { active[id] = Task.Run(() => work(id, ct), ct); next = index + 1; }
            }
        }
    }
}
