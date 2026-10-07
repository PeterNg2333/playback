using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;

// The Phase 2 deadline replaces the original stalled-wait characterization.
// Flat notes and small dirty organization remain later-phase baselines.
static class RefactorBaselineChecks
{
    public static async Task Run()
    {
        await UnchangedWait();
        await DiagnosticDecision();
        FlatNotesAndOrganization();
        Serialization();
        Console.WriteLine("Refactor baseline passed: saved wait admitted by deadline, small dirty notes not organized, flat TCP shape, legacy BSON/HTTP readback");
    }

    static async Task DiagnosticDecision()
    {
        var oldKey = Environment.GetEnvironmentVariable("JEV_API_KEY");
        var oldOffline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        try
        {
            Environment.SetEnvironmentVariable("JEV_API_KEY", "offline-fixture");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            var handler = new NoteGateHandler { Probability = .55, Confidence = .26 };
            using var transport = new JevTransport(handler);
            var decision = await new JevNoteGate(transport).Decide("Synthetic TCP pending input", CancellationToken.None);
            Expect.That(!decision.Allow && decision.Probability == .55 && decision.Confidence == .26,
                "The diagnostic probability/confidence no longer reject under the baseline thresholds");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JEV_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", oldOffline);
        }
    }

    static async Task UnchangedWait()
    {
        var store = new MemoryStore();
        store.Add("baseline-wait");
        store.Speech("baseline-wait", "fragment", "This unresolved example is meaningful but still needs continuation.");
        var gate = new FixtureNoteGate { Allow = false };
        var model = new FixtureNoteModel();
        var clock = new TestClock();
        await using (var agent = new NoteAgent(store, model, gate, NullLogger<NoteAgent>.Instance, new AiActivity(store), clock))
        {
            clock.Advance(10);
            agent.Tick(["baseline-wait"], CancellationToken.None);
            await agent.Drain();
            clock.Advance(600);
            agent.Tick(["baseline-wait"], CancellationToken.None);
            await agent.Drain();
        }
        await using (var restarted = new NoteAgent(store, model, gate, NullLogger<NoteAgent>.Instance, new AiActivity(store), clock))
        {
            clock.Advance(10);
            restarted.Tick(["baseline-wait"], CancellationToken.None);
            await restarted.Drain();
        }
        Expect.That(gate.Calls == 1 && model.Calls == 1 && store.Gates["baseline-wait"].Status == "completed" &&
            store.Gates["baseline-wait"].Decision == "wait" && store.Gates["baseline-wait"].AdmissionReason == "deadline",
            "Local deadline failed to admit a saved wait or fabricated a Jev allow");
        Expect.That(store.Transcripts["baseline-wait"].Single().NoteStatus == "completed", "Deadline lost source evidence");
    }

    static void FlatNotesAndOrganization()
    {
        // Synthetic reconstruction of the shape reported in the plan, not a copy of private Mongo data.
        var note = new Note { SessionId = "tcp-fixture", Version = 1, Author = "agent",
            Markdown = "## Transmission Control Protocol (TCP)\n\nReliable delivery and connection setup. [purpose]\n\nRetransmission and flow control. [reliability]" };
        var sections = NoteSections.Read(note, ["purpose", "reliability"]);
        Expect.That(sections.Count == 1 && sections[0].Points.Count == 2 && !sections[0].Markdown.Contains("###"),
            "Legacy TCP example was silently split into a guessed hierarchy");
        Expect.That(!NoteAgent.ShouldOrganize(sections[0]), "Small dirty notes gained Phase 5 automatic organization");
        Expect.That(NoteSections.Render(sections) == note.Markdown, "Legacy reading changed saved Markdown");
    }

    static void Serialization()
    {
        const string legacy = "{ '_id': 'legacy', 'SessionId': 's', 'Version': 7, 'Markdown': '## Notes\\n\\nR/n [source]', 'Author': 'user' }";
        var note = BsonSerializer.Deserialize<Note>(BsonDocument.Parse(legacy));
        Expect.That(note.Id == "legacy" && note.Version == 7 && note.Sections.Count == 0 && note.Coverage.Count == 0,
            "Old notes without sections/coverage stopped reading");
        var section = NoteSections.Read(note, ["source"]).Single();
        note.Sections = [section];
        var bson = note.ToBsonDocument();
        var roundTrip = BsonSerializer.Deserialize<Note>(bson);
        Expect.That(bson.Contains("_id") && !bson.Contains("Id") && !bson.Contains("_t") &&
            bson["Sections"][0].AsBsonDocument.Contains("_id") && roundTrip.Sections[0].Id == section.Id &&
            roundTrip.Sections[0].Points[0].Text == section.Points[0].Text,
            "Note extraction changed BSON IDs, discriminator or nested point readback");
        var json = JsonSerializer.SerializeToElement(note, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Expect.That(json.GetProperty("id").GetString() == "legacy" && json.GetProperty("version").GetInt32() == 7 &&
            json.GetProperty("sections")[0].GetProperty("userEdited").GetBoolean(), "HTTP note contract changed");
        var gate = BsonSerializer.Deserialize<NoteGateRecord>(BsonDocument.Parse("{ '_id': 's', 'SessionId': 's', 'Status': 'wait', 'InputHash': 'same' }"));
        Expect.That(gate.Id == "s" && gate.Status == "wait" && gate.FlushVersion == 0 &&
            BsonSerializer.Deserialize<NoteGateRecord>(gate.ToBson()).InputHash == "same", "Saved gate migration lost its wait identity");
    }
}
