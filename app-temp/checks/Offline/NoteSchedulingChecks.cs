using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;

// When automatic notes run: the real 10-second tick, a saved wait that survives a restart, Stop and manual
// revisions, slow or failing providers, races with edits and language changes, and fair admission.
static class NoteSchedulingChecks
{
    public static async Task Run()
    {
        await Schedule();
        await GateProtocol();
        await FairAdmission();
        Console.WriteLine("Note scheduling checks passed: actual 10 s tick, saved wait/allow across restart, idle sessions free, Stop and manual revise, " +
            "slow session isolated, failures retried with backoff, edit/language races rejected, fair admission, note-gate protocol");
    }

    static async Task Schedule()
    {
        var store = new MemoryStore();
        var clock = new TestClock();
        var model = new FixtureNoteModel();
        var gate = new FixtureNoteGate();
        var activity = new AiActivity(store);
        NoteAgent Agent() => new(store, model, gate, NullLogger<NoteAgent>.Instance, activity, clock);
        async Task Tick(NoteAgent agent, double seconds, params string[] ids)
        {
            clock.Advance(seconds);
            agent.Tick(ids, CancellationToken.None);
            await agent.Drain().WaitAsync(TimeSpan.FromSeconds(5));
        }

        store.Add("a");
        store.Speech("a", "a1", "The capacity R is shared among flows on the same bottleneck.");
        await using (var agent = Agent())
        {
            await Tick(agent, 9.9, "a");
            Expect.That(gate.Calls == 0, "Gate ran before the actual 10-second tick");
            await Tick(agent, .1, "a");
            Expect.That(gate.Calls == 1 && model.Calls == 1 && store.Notes["a"].Version == 1, "Allow did not immediately generate exactly one note");
            await Tick(agent, 10, "a");
            Expect.That(gate.Calls == 1 && model.Calls == 1, "Idle session called a provider");
            store.Add("b");
            store.Speech("b", "b1", "An incomplete mathematical example uses a shared capacity of");
            gate.Allow = false;
            await Tick(agent, 10, "b");
            Expect.That(model.Calls == 1 && store.Gates["b"].Status == "wait" && store.Transcripts["b"].Single().NoteStatus != "completed",
                "Wait completed or discarded pending speech");
            await Tick(agent, 10, "b");
            Expect.That(gate.Calls == 2, "Same waiting input was evaluated again");
        }

        await using (var restarted = Agent())
        {
            await Tick(restarted, 10, "b");
            Expect.That(gate.Calls == 2, "Restart forgot the saved wait identity");
            store.Speech("b", "b2", "R shared by n flows, and for ten flows each targets R/10.");
            gate.Allow = true;
            await Tick(restarted, 10, "b");
            Expect.That(gate.Calls == 3 && model.Calls == 2 && gate.Inputs.Any(x => x.Contains("incomplete") && x.Contains("ten flows")),
                "Continuation did not accumulate complete pending input");

            store.Add("c");
            store.Speech("c", "c1", "Stop should retain meaningful speech even if it remains uncertain.");
            gate.Allow = false;
            store.Gates["c"] = new NoteGateRecord { SessionId = "c", FlushRequested = true };
            await Tick(restarted, 10, "c");
            Expect.That(model.Calls == 3 && store.Gates["c"].Decision == "wait", "Stop falsely reported a Jev allow or failed to flush meaningful input");

            store.Add("d");
            store.Speech("d", "d1", "Manual revision must work immediately without awaiting a note gate tick.");
            await restarted.Generate("d", CancellationToken.None);
            Expect.That(model.Calls == 4 && gate.Calls == 4, "Manual Revise waited for Jev");

            store.Add("e");
            store.Speech("e", "e1", "A slow session cannot block the other lecture from getting its notes.");
            store.Add("f");
            store.Speech("f", "f1", "Another lecture can finish its example while the first is waiting.");
            gate.Allow = true;
            gate.BlockSession = "e";
            clock.Advance(10);
            restarted.Tick(["e", "f"], CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var i = 0; i < 100 && !store.Notes.ContainsKey("f"); i++) await Task.Delay(10);
            Expect.That(store.Notes.ContainsKey("f") && !store.Notes.ContainsKey("e"), "Slow Jev blocked unrelated sessions");
            clock.Advance(10);
            restarted.Tick(["e"], CancellationToken.None);
            Expect.That(gate.Inputs.Count(x => x.Contains("slow session")) == 1, "An active session was admitted twice");
            gate.Release.TrySetResult();
            await restarted.Drain().WaitAsync(TimeSpan.FromSeconds(3));
            gate.BlockSession = null;

            store.Add("g");
            store.Speech("g", "g1", "Provider failures must retain this source and be visible in activity.");
            gate.FailNext = true;
            await Tick(restarted, 10, "g");
            Expect.That(store.Gates["g"].Status == "failed" && store.Activity.Values.Any(x => x.SessionId == "g" && x.Status == "failed"),
                "Gate error became a fabricated success");
            var calls = gate.Calls;
            await Tick(restarted, 10, "g");
            Expect.That(gate.Calls == calls, "Error retried without backoff");
            await Tick(restarted, 30, "g");
            Expect.That(store.Notes.ContainsKey("g"), "Retry did not recover retained input");

            store.Add("h");
            store.Speech("h", "h1", "An allowed gate decision should survive a generation provider failure.");
            model.FailNext = true;
            await Tick(restarted, 10, "h");
            calls = gate.Calls;
            await Tick(restarted, 30, "h");
            Expect.That(store.Notes.ContainsKey("h") && gate.Calls == calls, "LLM retry paid for the same Jev decision again");

            store.Add("race");
            store.Speech("race", "r1", "An edit or output-language change during generation invalidates the base.");
            model.BeforeFinish = id =>
            {
                if (id == "race") store.Languages[id] = "en";
                return Task.CompletedTask;
            };
            await Tick(restarted, 10, "race");
            Expect.That(!store.Notes.ContainsKey("race") && store.Gates["race"].Status == "failed", "An old-language result overwrote new settings");
            model.BeforeFinish = id =>
            {
                if (id == "race") store.Notes[id] = new Note { Version = 10, Markdown = "User edit", SessionId = id };
                return Task.CompletedTask;
            };
            store.Languages["race"] = "zh-Hant";
            await Tick(restarted, 30, "race");
            Expect.That(store.Notes["race"].Version == 10 && store.Notes["race"].Markdown == "User edit", "An old note-base result overwrote user edits");
            model.BeforeFinish = null;
        }
    }

    // The gate parses Jev's note-specific answer, not a glossary probability, and discovers its model once.
    static async Task GateProtocol()
    {
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY");
        var offline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        try
        {
            Environment.SetEnvironmentVariable("JEV_API_KEY", "offline-fixture");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            var handler = new NoteGateHandler();
            using var transport = new JevTransport(handler);
            var gate = new JevNoteGate(transport);
            var decision = await gate.Decide("Pending complete example", CancellationToken.None);
            Expect.That(decision.Allow && decision.Model == "fixture-version" && decision.UsageJson!.Contains("output_tokens"),
                "Gate did not parse the actual note-specific structured response");
            Expect.That(handler.Request!.Contains("\"action\"") && handler.Request.Contains("\"update\"") && !handler.Request.Contains("\"explain\""),
                "Gate reused a glossary probability");
            handler.Confidence = .2;
            Expect.That(!(await gate.Decide("Low confidence", CancellationToken.None)).Allow, "Uncertain choice became an allow");
            handler.Invalid = true;
            await Expect.RejectsAsync(() => gate.Decide("Bad response", CancellationToken.None), "Invalid probability accepted");
            Expect.That(handler.Models == 1, "Shared transport rediscovered its model for every gate call");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JEV_API_KEY", key);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", offline);
        }
    }

    static async Task FairAdmission()
    {
        var clock = new TestClock();
        var visited = new ConcurrentBag<string>();
        var scheduler = new NoteScheduler(clock, (id, _) =>
        {
            visited.Add(id);
            return Task.CompletedTask;
        });
        clock.Advance(10);
        scheduler.Tick(["a", "b", "c", "d", "e", "f"], CancellationToken.None);
        await Task.WhenAll(scheduler.Active);
        clock.Advance(10);
        scheduler.Tick(["a", "b", "c", "d", "e", "f"], CancellationToken.None);
        await Task.WhenAll(scheduler.Active);
        Expect.That(visited.Distinct().Count() == 6, "Four repeatedly waiting sessions starved later sessions");
    }
}
