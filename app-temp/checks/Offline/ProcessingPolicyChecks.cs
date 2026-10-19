using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Application.Notes;
using Playback.Domain.Notes;
using System.Text.Json;

static class ProcessingPolicyChecks
{
    public static async Task Run()
    {
        await Settings();
        await DeadlineAndRestart();
        await ThresholdChange();
        await DeferralAndFailures();
        await DurableStop();
        await Sync();
        Console.WriteLine("Processing policy checks passed: validated persisted defaults/overrides, revision conflicts, workspace isolation, configured cadence/deadline across restart, threshold invalidation, gate disabled, no filler loop, failed gate deadline, bounded writer retry, concurrent Stop + late ASR, incremental settings/admission sync");
    }
    static async Task Settings()
    {
        var repository = new MemoryProcessingRepository();
        var settings = new ProcessingSettings(repository, ProcessingPolicy.Defaults);
        var alice = await settings.SaveWorkspace("alice", new(0, new(JevEnabled: false, MaxPendingWaitSeconds: 20)));
        Expect.That(alice.Revision == 1 && !alice.Effective.JevEnabled && (await settings.Workspace("bob")).Revision == 0, "Workspace defaults leaked between owners");
        var session = await settings.SaveSession("alice", "a", new(0, new(ProbabilityThreshold: 0, JevEnabled: true), 1));
        Expect.That(session.Effective.ProbabilityThreshold == 0 && session.Effective.JevEnabled && session.Effective.MaxPendingWaitSeconds == 20, "Explicit zero/true was treated as missing");
        await settings.SaveWorkspace("alice", new(1, new(JevEnabled: false, MaxPendingWaitSeconds: 25)));
        var restarted = new ProcessingSettings(repository, ProcessingPolicy.Defaults);
        session = await restarted.Session("alice", "a");
        Expect.That(session.Effective.MaxPendingWaitSeconds == 25 && session.Effective.JevEnabled && session.EffectiveRevision == "2:1", "Restart lost inheritance or explicit overrides");
        await Expect.RejectsAsync(() => settings.SaveSession("alice", "a", new(1, new(), 1)), "Stale inherited revision accepted");
        await Expect.RejectsAsync(() => settings.SaveWorkspace("alice", new(1, new())), "Stale workspace revision accepted");
        await settings.SaveSession("alice", "a", new(1, new(), 2));
        Expect.That(!(await settings.Session("alice", "a")).Effective.JevEnabled, "Reset did not resume inheritance");
        var revision = (await settings.Workspace("alice")).Revision;
        await settings.SaveWorkspace("alice", new(revision, new(JevEnabled: false, MaxPendingWaitSeconds: 25)));
        Expect.That((await settings.Workspace("alice")).Revision == revision, "No-op save invalidated provider decisions");
        foreach (var invalid in new[] { new ProcessingOverrides(ProbabilityThreshold: double.NaN), new(ConfidenceThreshold: double.PositiveInfinity),
            new(CheckIntervalSeconds: 1), new(MaxPendingWaitSeconds: 121), new(SilenceBoundarySeconds: .6), new(BoundarySettleSeconds: -1) })
        {
            try { await settings.SaveWorkspace("alice", new(revision, invalid)); throw new CheckFailed("Invalid policy accepted"); }
            catch (ProcessingPolicyValidationException) { }
        }
        var writes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await repository.Save(new() { Id = "race", Revision = 1 }, 0); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Expect.That(writes.Count(x => x) == 1, "Concurrent first settings writes both succeeded");
        var doc = (await repository.Read("session:a"))!.ToBsonDocument();
        var roundTrip = BsonSerializer.Deserialize<ProcessingSettingsRecord>(doc);
        Expect.That(roundTrip.SchemaVersion == 1 && roundTrip.Revision == 2 && roundTrip.Overrides.JevEnabled is null, "BSON settings round-trip lost missing fields/revisions");
        var old = BsonSerializer.Deserialize<NoteGateRecord>(new BsonDocument { { "_id", "old" }, { "SessionId", "old" }, { "Status", "wait" } });
        Expect.That(old.FirstPendingAt is null && old.PolicyRevision == "", "Legacy work state became unreadable");
    }
    static NoteAgent Agent(MemoryStore store, FixtureNoteModel model, FixtureNoteGate gate, TestClock clock) =>
        new(store, model, gate, NullLogger<NoteAgent>.Instance, new AiActivity(store), clock);
    static async Task Tick(NoteAgent agent, TestClock clock, double seconds, string id)
    {
        clock.Advance(seconds); agent.Tick([id], CancellationToken.None); await agent.Drain().WaitAsync(TimeSpan.FromSeconds(5));
    }
    static async Task Configure(MemoryStore store, string id, ProcessingOverrides value)
    {
        var saved = await store.SessionProcessingSettings(id);
        await store.SaveSessionProcessingSettings(id, new(saved.Revision, value, saved.InheritedRevision));
    }
    static async Task DeadlineAndRestart()
    {
        var store = new MemoryStore(); store.Add("deadline"); store.Speech("deadline", "s", "A short unresolved example.");
        await Configure(store, "deadline", new(CheckIntervalSeconds: 2, MaxPendingWaitSeconds: 10));
        var clock = new TestClock(); var model = new FixtureNoteModel(); var gate = new FixtureNoteGate { Allow = false };
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "deadline");
            Expect.That(gate.Calls == 1 && model.Calls == 0 && store.Gates["deadline"].FirstPendingAt == clock.GetUtcNow().UtcDateTime, "Configured cadence or durable first-pending timestamp failed");
            await Tick(agent, clock, 8, "deadline");
            Expect.That(model.Calls == 0, "Writer admitted before the configured deadline");
        }
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "deadline");
            Expect.That(model.Calls == 1 && gate.Calls == 1 && store.Gates["deadline"].Decision == "wait" && store.Gates["deadline"].AdmissionReason == "deadline", "Restart reset the deadline or fabricated a Jev allow");
        }
        store.Add("disabled"); store.Speech("disabled", "s2", "Short speech still writes.");
        await Configure(store, "disabled", new(JevEnabled: false, CheckIntervalSeconds: 2, MaxPendingWaitSeconds: 10));
        await using (var agent = Agent(store, model, gate, clock))
        {
            // Without Jev, speech waits for its batch deadline and is then written, however short.
            await Tick(agent, clock, 2, "disabled");
            Expect.That(gate.Calls == 1 && model.Calls == 1 && store.Gates["disabled"].Status == "batching", "Batching wrote early or asked Jev");
            await Tick(agent, clock, 10, "disabled");
            Expect.That(gate.Calls == 1 && model.Calls == 2 && store.Gates["disabled"].AdmissionReason == "deadline", "A batch without Jev was not written at its deadline or asked Jev");
        }
    }
    static async Task ThresholdChange()
    {
        var store = new MemoryStore(); store.Add("threshold"); store.Speech("threshold", "s", "The diagnostic TCP probability is not a general quality benchmark.");
        var clock = new TestClock(); var model = new FixtureNoteModel();
        var gate = new FixtureNoteGate { Allow = false, Action = "update", Probability = .55, Confidence = .26 };
        await using (var agent = Agent(store, model, gate, clock)) await Tick(agent, clock, 10, "threshold");
        Expect.That(model.Calls == 0 && store.Gates["threshold"].Decision == "wait", "Default thresholds changed");
        await Configure(store, "threshold", new(ProbabilityThreshold: .5, ConfidenceThreshold: .2));
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "threshold");
            Expect.That(gate.Calls == 2 && model.Calls == 1 && store.Gates["threshold"].PolicyRevision == "0:1" && store.Gates["threshold"].AdmissionReason == "jev-allow", "Policy revision/restart reused a stale wait");
        }
    }
    static async Task DeferralAndFailures()
    {
        var store = new MemoryStore(); store.Add("filler"); store.Speech("filler", "s", "The next example begins with");
        await Configure(store, "filler", new(CheckIntervalSeconds: 2, MaxPendingWaitSeconds: 10));
        var clock = new TestClock(); var model = new FixtureNoteModel { DeferAll = true }; var gate = new FixtureNoteGate { Allow = false };
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "filler"); await Tick(agent, clock, 10, "filler");
            for (var i = 0; i < 12; i++) await Tick(agent, clock, 10, "filler");
        }
        await using (var agent = Agent(store, model, gate, clock)) await Tick(agent, clock, 10, "filler");
        Expect.That(model.Calls == 1 && gate.Calls == 1 && store.Transcripts["filler"][0].NoteStatus == "deferred" && !store.Notes.ContainsKey("filler"), "Deferred filler entered a repeated paid loop or became coverage");
        model.DeferAll = false;
        store.Add("input-failure"); store.Speech("input-failure", "oversized", new string('字', 10000));
        await Configure(store, "input-failure", new(CheckIntervalSeconds: 2));
        var gateCalls = gate.Calls; var writerCalls = model.Calls;
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "input-failure");
            for (var i = 0; i < 5; i++) await Tick(agent, clock, 2, "input-failure");
            Expect.That(store.Gates["input-failure"].Attempts == 1 && gate.Calls == gateCalls && model.Calls == writerCalls,
                "Input preparation failure ignored backoff or called a provider");
        }
        store.Add("gate-failure"); store.Speech("gate-failure", "s2", "Retained evidence survives a Jev failure.");
        await Configure(store, "gate-failure", new(CheckIntervalSeconds: 2, MaxPendingWaitSeconds: 10)); gate.FailNext = true;
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "gate-failure");
            Expect.That(store.Gates["gate-failure"].Status == "failed", "Jev failure became success");
            await Tick(agent, clock, 10, "gate-failure");
            Expect.That(gate.Calls == 2 && model.Calls == 2 && store.Gates["gate-failure"].AdmissionReason == "deadline-after-gate-failure" && store.Gates["gate-failure"].Decision != "allow", "Gate backoff blocked the deadline or fabricated allow");
        }
        store.Add("writer-failure"); store.Speech("writer-failure", "s3", "Writer retries reuse local admission.");
        await Configure(store, "writer-failure", new(JevEnabled: false, CheckIntervalSeconds: 2, MaxPendingWaitSeconds: 10));
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 2, "writer-failure"); // starts the batch
            for (var i = 0; i < 3; i++) { model.FailNext = true; await Tick(agent, clock, i == 0 ? 10 : i * 30, "writer-failure"); }
            var calls = model.Calls; await Tick(agent, clock, 300, "writer-failure");
            Expect.That(model.Calls == calls && store.Gates["writer-failure"].Attempts == 3 && store.Transcripts["writer-failure"][0].NoteStatus == "pending", "Writer failure exceeded retry bounds or lost sources");
            model.FailNext = false; await agent.Generate("writer-failure", CancellationToken.None);
            Expect.That(store.Notes.ContainsKey("writer-failure"), "Explicit revision could not recover an exhausted automatic retry");
        }
    }
    static async Task DurableStop()
    {
        var store = new MemoryStore(); store.Add("stop"); store.Speech("stop", "first", "The first finalized sentence.");
        await store.RequestNoteFlush("stop");
        var clock = new TestClock(); var gate = new FixtureNoteGate { Allow = false }; var model = new FixtureNoteModel();
        model.BeforeFinish = async _ =>
        {
            await store.RequestNoteFlush("stop");
            store.Chunks["stop"].Add(new ChunkRecord { Id = "late", Status = ChunkStatus.PendingAsr });
        };
        await using (var agent = Agent(store, model, gate, clock)) await Tick(agent, clock, 2, "stop");
        Expect.That(store.Gates["stop"].FlushRequested && store.Gates["stop"].FlushVersion == 2, "In-flight decision save lost a newer Stop");
        model.BeforeFinish = null;
        await using (var agent = Agent(store, model, gate, clock))
        {
            await Tick(agent, clock, 10, "stop");
            Expect.That(store.Gates["stop"].FlushRequested && model.Calls == 1, "Restart acknowledged Stop before final ASR");
            store.Chunks["stop"][0].Status = ChunkStatus.Transcribed; store.Speech("stop", "final", "The late final ASR belongs to Stop.");
            await Tick(agent, clock, 10, "stop");
            Expect.That(!store.Gates["stop"].FlushRequested && model.Calls == 2 && gate.Calls == 0 && store.Notes["stop"].Markdown.Contains("late final"), "Stop lost late evidence or unnecessarily called Jev");
        }
    }
    static async Task Sync()
    {
        var store = new MemoryStore(); store.Add("sync");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var first = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("sync", null), options));
        var cursor = first.RootElement.GetProperty("cursor").GetString();
        await Configure(store, "sync", new(MaxPendingWaitSeconds: 20));
        using var next = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("sync", cursor), options));
        var changes = next.RootElement.GetProperty("changes");
        Expect.That(changes.GetArrayLength() == 1 && changes[0].GetProperty("kind").GetString() == "processing" &&
            next.RootElement.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1, "Settings sync reloaded the lecture or omitted the effective policy");
        await store.SaveNoteGate(new() { SessionId = "sync", Status = "wait", LastCheckedAt = DateTime.UtcNow });
        await store.SyncSession("sync", cursor);
        var state = (await store.NoteGate("sync"))!;
        state.LastCheckedAt = state.LastCheckedAt!.Value.AddSeconds(10);
        await store.SaveNoteGate(state);
        using var idle = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("sync", cursor), options));
        Expect.That(idle.RootElement.GetProperty("changes").GetArrayLength() == 0, "Scheduler bookkeeping published an idle UI delta");
    }
}
