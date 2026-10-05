using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Endpoints;

// In-memory lifecycle checks only: never removes a database record or audio file.
static class SessionDeletionChecks
{
    public static async Task Run()
    {
        Expect.That(new CaptureInput("session").SourceMode == "microphone", "Omitted recording source must select microphone only");
        var store = new MemoryStore();
        var activity = new AiActivity(store);
        var queued = await activity.Begin("session", "ASR", "fixture", "fixture");
        await Expect.RejectsAsync(() => activity.DeleteSession("session"), "Queued work must block deletion");
        Expect.That(store.Deleted.Count == 0, "Blocked deletion reached storage");
        await activity.End(queued, "completed", "fixture");
        store.ReleaseDeletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleting = activity.DeleteSession("session");
        await Expect.RejectsAsync(() => activity.Begin("session", "Notes", "fixture", "fixture"), "New AI work must not start during deletion");
        var other = await activity.Begin("other-session", "Notes", "fixture", "fixture");
        await activity.End(other, "completed", "fixture");
        store.ReleaseDeletion.SetResult();
        await deleting;
        await Expect.RejectsAsync(() => activity.Begin("session", "ASR", "fixture", "fixture"), "Queued callbacks must not revive a deleted session");
        Expect.That(store.Deleted.SequenceEqual(["session"]), "Deletion affected the wrong session");

        store.FailDeletion = true;
        await Expect.RejectsAsync(() => activity.DeleteSession("retry-session"), "Storage failure must reach the caller");
        var retry = await activity.Begin("retry-session", "Notes", "fixture", "fixture");
        await activity.End(retry, "completed", "fixture");
        store.FailDeletion = false;
        await activity.DeleteSession("retry-session");
        Expect.That(store.Deleted.SequenceEqual(["session", "retry-session"]), "Failed deletion could not be retried");
        Console.WriteLine("Session deletion checks passed: queued work, concurrent admission, session isolation, storage failure/retry and microphone default (in-memory only)");
    }

    sealed class MemoryStore : PlaybackStore
    {
        public List<string> Deleted { get; } = [];
        public TaskCompletionSource? ReleaseDeletion;
        public bool FailDeletion;
        public override Task SaveActivity(ActivityRecord item) => Task.CompletedTask;
        public override async Task DeleteSession(string id)
        {
            if (FailDeletion) throw new InvalidOperationException("Fixture deletion failure");
            if (ReleaseDeletion is not null) await ReleaseDeletion.Task;
            Deleted.Add(id);
        }
    }
}
