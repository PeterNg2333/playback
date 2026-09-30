using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Db;
using Playback.Api.Services.Ai;
using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;

internal static partial class NotesRedesignCheck
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action work, string message) { try { work(); } catch (InvalidOperationException) { return; } throw new Exception(message); }
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static async Task Run() {
        var offline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        try { Sections(); await CoverageSafeguards(); await Scheduling(); await OrganizationAndLateInput(); await GateProtocol(); await Sync(); await FairAdmission(); }
        finally { Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", offline); }
        Console.WriteLine("Notes redesign checks passed: section retention/coverage, protected edits/deletions/recovery, actual 10s scheduling, persisted wait/allow, idle/dedup, slow sessions, errors/retries, manual/stop and edit/language races (memory providers/store, no network).");
    }
    static async Task FairAdmission() {
        var clock = new TestClock(); var visited = new ConcurrentBag<string>();
        var scheduler = new NoteScheduler(clock, (id, _) => { visited.Add(id); return Task.CompletedTask; });
        clock.Advance(10); scheduler.Tick(["a", "b", "c", "d", "e", "f"], CancellationToken.None); await Task.WhenAll(scheduler.Active);
        clock.Advance(10); scheduler.Tick(["a", "b", "c", "d", "e", "f"], CancellationToken.None); await Task.WhenAll(scheduler.Active);
        Check(visited.Distinct().Count() == 6, "Four repeatedly waiting sessions starved later sessions");
    }
    static void Sections() {
        var execution = new ActivityRecord { Id = "wire-check", Task = "Note revision", PromptText = new string('x', 100000),
            Draft = "Readable live draft", PromptHash = "exact-instructions-hash", SourceIds = ["source-check"] };
        var compactActivity = JsonSerializer.Serialize(new[] { execution }, AiActivity.NotesResponseOptions);
        Check(compactActivity.Length < 2000 && !compactActivity.Contains("promptText") && compactActivity.Contains("Readable live draft") &&
            compactActivity.Contains("exact-instructions-hash") && JsonSerializer.Serialize(execution, JsonOptions).Contains("promptText") &&
            execution.PromptText.Length == 100000, "Notes polling lost required metadata or discarded the saved/group prompt");
        Check(NoteAgent.ReadableDraft("{\"sections\":[{\"markdown\":\"## Topic\\n\\nReadable partial") == "## Topic\n\nReadable partial",
            "Structured stream exposed raw JSON instead of a readable Markdown draft");
        var material = new Material { Id = new string('b', 32), Text = new string('x', 14500), Name = "Long material" };
        var passages = MaterialSources.Passages([material]).ToList();
        Check(passages.Count == 5 && string.Concat(passages.Select(x => x.Text)) == material.Text && passages.All(x => x.Text.Length <= 3000) &&
            passages.All(x => MaterialSources.Parent(x.Id) == material.Id), "Bounded material passages lost text or source identity");
        var early = new NoteSection { Id = "fair", Title = "Fair sharing", Markdown = "## Fair sharing\n\nThe target is R/n for n flows sharing one bottleneck; n=10 gives R/10. [early]",
            Points = [new NotePoint { Id = "formula", Text = "The target is R/n for n flows sharing one bottleneck; n=10 gives R/10. [early]", SourceIds = ["early"] }] };
        var security = new NoteSection { Id = "security", Title = "Security", Markdown = "## Security\n\nEncryption and encapsulation are distinct. [security-source]",
            Points = [new NotePoint { Id = "layers", Text = "Encryption and encapsulation are distinct. [security-source]", SourceIds = ["security-source"] }] };
        var note = new Note { SessionId = "s", Id = "old", Version = 112, Sections = [early, security], Markdown = NoteSections.Render([early, security]) };
        var add = new NotePatch { Sections = [new SectionPatch { Id = "new", Title = "Edge", Markdown = "## Edge\n\nNew edge topic. [late]",
            Points = [new PointPatch { Text = "New edge topic. [late]", SourceIds = ["late"] }] }] };
        var update = NoteSections.Apply("s", note, note.Sections, add, [], ["late"], ["early", "security-source", "late"], "hash");
        Check(update.Sections[0] == early && update.Sections[1] == security && NoteSections.Render(update.Sections).Contains("R/10"), "Appending a topic removed an old formula/section");
        Check(update.Coverage.Single().PointIds.Count == 1 && update.Citations.Single().SourceIds.SequenceEqual(["late"]), "Coverage or durable citation was not tied to the actual point");
        var multipleNew = new NotePatch { Sections = [add.Sections[0], new SectionPatch { Id = "new", Title = "Continuation",
            Markdown = "## Continuation\n\nAnother complete topic. [later]", Points = [new PointPatch { Text = "Another complete topic. [later]", SourceIds = ["later"] }] }] };
        var appended = NoteSections.Apply("s", note, note.Sections, multipleNew, [], ["late", "later"], ["early", "security-source", "late", "later"], "two-new");
        Check(appended.Sections.Count == 4 && appended.Sections.Select(x => x.Id).Distinct().Count() == 4 && appended.Coverage.Count == 2,
            "Two new topics using the literal new identity collided or lost coverage");
        var partial = NoteSections.Apply("s", note, note.Sections, add, [], ["late", "unaddressed"], ["early", "security-source", "late", "unaddressed"], "partial");
        Check(partial.Coverage.Single(x => x.SourceId == "unaddressed").Status == "pending" && partial.Coverage.Single(x => x.SourceId == "unaddressed").PointIds.Count == 0 &&
            partial.Sections.Count == 3, "A valid partial section was discarded, or an omitted source was falsely marked completed/deferred");
        Check(NoteAgent.ReadableDraft("{\"sections\":[{\"points\":[{\"text\":\"First point [T001]\"},{\"text\":\"Second point") ==
            "First point [T001]\n\nSecond point", "Canonical point stream lost readable draft content");
        Reject(() => NoteSections.Apply("s", note, note.Sections, new NotePatch { Sections = [new SectionPatch { Id = "invented-topic" }] }, [], [], [], "x"),
            "An invented section identity was accepted");
        var bad = new NotePatch { Sections = [new SectionPatch { Id = "fair", BaseVersion = 1, Title = "Fair", Markdown = "## Fair\n\nSome sharing. [early]",
            Points = [new PointPatch { Text = "Some sharing. [early]", SourceIds = ["early"] }] }] };
        Reject(() => NoteSections.Apply("s", note, note.Sections, bad, ["fair"], [], ["early"], "x"), "Dropping a coverage point was accepted");
        bad.Sections[0].Points[0].Retains = ["formula"];
        var safeguarded = NoteSections.Apply("s", note, note.Sections, bad, ["fair"], [], ["early"], "y");
        Check(NoteSections.Render(safeguarded.Sections).Contains("R/n") && NoteSections.Render(safeguarded.Sections).Contains("n=10"), "A model retaining an ID while dropping its formula bypassed literal retention");
        early.UserEdited = true;
        Reject(() => NoteSections.Apply("s", note, note.Sections, bad, ["fair"], [], ["early"], "x"), "Automatic generation replaced a user-edited section");
        early.UserEdited = false;
        Reject(() => NoteSections.Apply("s", note, note.Sections, new NotePatch(), [], ["late"], ["late"], "x"), "Metadata-only completion was accepted");
        var deferred = NoteSections.Apply("s", note, note.Sections, new NotePatch { Deferred = [new SourceDisposition { SourceId = "half", Reason = "Formula continues in the next chunk" }] },
            [], ["half"], ["half"], "x");
        Check(deferred.Coverage.Single().Status == "deferred", "Incomplete fragments were completed");
        var compact = NoteSections.Compact("s", "One point [early, security-source]", ["early", "security-source"], update.Citations);
        Check(NoteSections.Expand(compact, update.Citations) == "One point [early, security-source]" &&
            NoteSections.Compact("s", "One point [security-source, early]", ["early", "security-source"], update.Citations) == compact,
            "Persistent citation identity depended on ordering or renumbering");
        var deleted = new List<string>(); var suppressed = new List<string>();
        var edited = NoteSections.UserEdit(note, "s", security.Markdown, ["early", "security-source"], [], deleted, suppressed);
        Check(deleted.Contains("fair") && suppressed.Contains("early") && edited.Single() == security, "Intentional deletion was not recorded: " + JsonSerializer.Serialize(new { deleted, suppressed, edited }));
        var latest = new Note { Id = "new", Version = 113, SessionId = "s", Sections = edited, DeletedSectionIds = deleted, SuppressedSourceIds = suppressed };
        Check(!NoteSections.Recover(note, latest, ["early", "security-source"]).Candidates.Any(x => x.MissingSourceIds.Contains("early")), "Recovery revived an intentional deletion");
        latest.DeletedSectionIds.Clear(); latest.SuppressedSourceIds.Clear();
        var preview = NoteSections.Recover(note, latest, ["early", "security-source"]);
        Check(preview.Candidates.Single().Markdown.Contains("R/10") && preview.CurrentSections.Single() == security && latest.Version == 113, "Recovery preview overwrote current notes or lost the formula");
        Check(!NoteAgent.ShouldOrganize(new NoteSection { UserEdited = true, Markdown = new string('x', 10000), Points = Enumerable.Range(0, 20).Select(_ => new NotePoint()).ToList() }),
            "Automatic organization included protected user content");
    }
    static async Task Sync() {
        var store = new MemoryStore(); store.Add("long"); store.Add("small");
        for (var i = 0; i < 2700; i++) store.Speech("long", "source-" + i, new string('x', 300));
        store.Notes["long"] = new Note { SessionId = "long", Markdown = new string('長', 200000) };
        string? cursor = null; var total = 0; var pages = 0;
        var fragmentCount = 0;
        while (true) {
            var json = JsonSerializer.Serialize(await store.SyncSession("long", cursor), JsonOptions);
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            Check(System.Text.Encoding.UTF8.GetByteCount(json) < 410000 && root.GetProperty("changes").GetArrayLength() <= 128, "Bootstrap sync was not bounded");
            total += root.GetProperty("changes").EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "transcript"); pages++;
            fragmentCount += root.GetProperty("changes").EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "fragment");
            cursor = root.GetProperty("cursor").GetString(); if (!root.GetProperty("hasMore").GetBoolean()) break;
        }
        Check(total == 2700 && pages > 20 && fragmentCount > 0, "Sync silently truncated a large record or the long transcript");
        using var idle = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("long", cursor), JsonOptions));
        Check(idle.RootElement.GetProperty("changes").GetArrayLength() == 0, "Idle sync re-sent a whole session");
        store.Transcripts["long"][1700].Translation = "One late translation"; store.Touch("long", "translation", ["source-1700"]);
        using var change = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("long", cursor), JsonOptions));
        Check(change.RootElement.GetProperty("changes").GetArrayLength() == 1, "One changed transcript re-sent unchanged records");
        Check(change.RootElement.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1 && store.SyncReadIds.Single().SequenceEqual(["source-1700"]),
            "A one-record delta read the full session or queried unrelated source IDs");
        using var reset = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("small", cursor), JsonOptions));
        Check(reset.RootElement.GetProperty("reset").GetBoolean(), "Sync cursor mixed sources from another session");
        store.Add("terms"); store.Speech("terms", "term-source", "TCP explains transport.");
        var term = new Playback.Api.Terms.TermCandidate("TCP", ["term-source"], [], "TCP explains transport.");
        store.Terms["terms"] = [term];
        var insightId = PlaybackStore.TermInsightId("terms", term.Text, term.Context);
        store.Insights["terms"] = [new TermInsight { Id = insightId, SessionId = "terms", Term = "TCP", Context = term.Context, TranscriptIds = ["term-source"] }];
        using var termBoot = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("terms", null), JsonOptions));
        var termCursor = termBoot.RootElement.GetProperty("cursor").GetString();
        store.Materials["terms"].Add(new Material { Id = "new-material", SessionId = "terms", Text = "**TCP** supports the earlier explanation." });
        store.Terms["terms"] = [term with { MaterialIds = ["new-material"] }];
        store.Touch("terms", "material", ["new-material"]);
        using var termDelta = JsonDocument.Parse(JsonSerializer.Serialize(await store.SyncSession("terms", termCursor), JsonOptions));
        var updatedInsight = termDelta.RootElement.GetProperty("changes").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "insight");
        Check(updatedInsight.GetProperty("value").GetProperty("materialIds")[0].GetString() == "new-material" &&
            termDelta.RootElement.GetProperty("diagnostics").GetProperty("fullReads").GetInt32() == 1,
            "Incremental term updates left an existing saved explanation's source references stale");
        Console.WriteLine($"Bounded sync: 2,700 sources + a 200,000-character Chinese note in {pages} pages / {fragmentCount} bounded fragments; idle=0 records; one changed source=1 record; cross-session cursor resets.");
    }
    static async Task Scheduling() {
        var store = new MemoryStore(); var clock = new TestClock(); var model = new FixtureModel(); var gate = new FixtureGate();
        var activity = new AiActivity(store);
        NoteAgent Agent() => new(store, model, gate, NullLogger<NoteAgent>.Instance, activity, clock);
        async Task Tick(NoteAgent agent, double seconds, params string[] ids) { clock.Advance(seconds); agent.Tick(ids, CancellationToken.None); await agent.Drain().WaitAsync(TimeSpan.FromSeconds(5)); }
        store.Add("a"); store.Speech("a", "a1", "The capacity R is shared among flows on the same bottleneck.");
        await using (var agent = Agent()) {
            await Tick(agent, 9.9, "a"); Check(gate.Calls == 0, "Gate ran before the actual 10-second tick");
            await Tick(agent, .1, "a"); Check(gate.Calls == 1 && model.Calls == 1 && store.Notes["a"].Version == 1, "Allow did not immediately generate exactly one note");
            await Tick(agent, 10, "a"); Check(gate.Calls == 1 && model.Calls == 1, "Idle session called a provider");
            store.Add("b"); store.Speech("b", "b1", "An incomplete mathematical example uses a shared capacity of"); gate.Allow = false;
            await Tick(agent, 10, "b"); Check(model.Calls == 1 && store.Gates["b"].Status == "wait" && store.Transcripts["b"].Single().NoteStatus != "completed", "Wait completed or discarded pending speech");
            await Tick(agent, 10, "b"); Check(gate.Calls == 2, "Same waiting input was evaluated again");
        }
        await using (var restarted = Agent()) {
            await Tick(restarted, 10, "b"); Check(gate.Calls == 2, "Restart forgot the saved wait identity");
            store.Speech("b", "b2", "R shared by n flows, and for ten flows each targets R/10."); gate.Allow = true;
            await Tick(restarted, 10, "b"); Check(gate.Calls == 3 && model.Calls == 2 && gate.Inputs.Any(x => x.Contains("incomplete") && x.Contains("ten flows")), "Continuation did not accumulate complete pending input");
            store.Add("c"); store.Speech("c", "c1", "Stop should retain meaningful speech even if it remains uncertain."); gate.Allow = false; store.Gates["c"] = new NoteGateRecord { SessionId = "c", FlushRequested = true };
            await Tick(restarted, 10, "c"); Check(model.Calls == 3 && store.Gates["c"].Decision == "wait", "Stop falsely reported a Jev allow or failed to flush meaningful input");
            store.Add("d"); store.Speech("d", "d1", "Manual revision must work immediately without awaiting a note gate tick.");
            await restarted.Generate("d", CancellationToken.None, true); Check(model.Calls == 4 && gate.Calls == 4, "Manual Revise waited for Jev");
            store.Add("e"); store.Speech("e", "e1", "A slow session cannot block the other lecture from getting its notes.");
            store.Add("f"); store.Speech("f", "f1", "Another lecture can finish its example while the first is waiting.");
            gate.Allow = true; gate.BlockSession = "e";
            clock.Advance(10); restarted.Tick(["e", "f"], CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var i = 0; i < 100 && !store.Notes.ContainsKey("f"); i++) await Task.Delay(10);
            Check(store.Notes.ContainsKey("f") && !store.Notes.ContainsKey("e"), "Slow Jev blocked unrelated sessions");
            clock.Advance(10); restarted.Tick(["e"], CancellationToken.None); Check(gate.Inputs.Count(x => x.Contains("slow session")) == 1, "An active session was admitted twice");
            gate.Release.TrySetResult(); await restarted.Drain().WaitAsync(TimeSpan.FromSeconds(3)); gate.BlockSession = null;
            store.Add("g"); store.Speech("g", "g1", "Provider failures must retain this source and be visible in activity."); gate.FailNext = true;
            await Tick(restarted, 10, "g"); Check(store.Gates["g"].Status == "failed" && store.Activity.Values.Any(x => x.SessionId == "g" && x.Status == "failed"), "Gate error became a fabricated success");
            var calls = gate.Calls; await Tick(restarted, 10, "g"); Check(gate.Calls == calls, "Error retried without backoff");
            await Tick(restarted, 30, "g"); Check(store.Notes.ContainsKey("g"), "Retry did not recover retained input");
            store.Add("h"); store.Speech("h", "h1", "An allowed gate decision should survive a generation provider failure."); model.FailNext = true;
            await Tick(restarted, 10, "h"); calls = gate.Calls; await Tick(restarted, 30, "h");
            Check(store.Notes.ContainsKey("h") && gate.Calls == calls, "LLM retry paid for the same Jev decision again");
            store.Add("race"); store.Speech("race", "r1", "An edit or output-language change during generation invalidates the base.");
            model.BeforeFinish = id => { if (id == "race") store.Languages[id] = "en"; return Task.CompletedTask; };
            await Tick(restarted, 10, "race"); Check(!store.Notes.ContainsKey("race") && store.Gates["race"].Status == "failed", "An old-language result overwrote new settings");
            model.BeforeFinish = id => { if (id == "race") store.Notes[id] = new Note { Version = 10, Markdown = "User edit", SessionId = id }; return Task.CompletedTask; };
            store.Languages["race"] = "zh-Hant"; await Tick(restarted, 30, "race");
            Check(store.Notes["race"].Version == 10 && store.Notes["race"].Markdown == "User edit", "An old note-base result overwrote user edits");
            model.BeforeFinish = null;
        }
    }
    static async Task GateProtocol() {
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY"); var offline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        try {
            Environment.SetEnvironmentVariable("JEV_API_KEY", "offline-fixture"); Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            var handler = new GateHandler(); using var transport = new JevTransport(handler); var gate = new JevNoteGate(transport);
            var decision = await gate.Decide("Pending complete example", CancellationToken.None);
            Check(decision.Allow && decision.Model == "fixture-version" && decision.UsageJson!.Contains("output_tokens"), "Gate did not parse the actual note-specific structured response");
            Check(handler.Request!.Contains("\"action\"") && handler.Request.Contains("\"update\"") && !handler.Request.Contains("\"explain\""), "Gate reused a glossary probability");
            handler.Confidence = .2; Check(!(await gate.Decide("Low confidence", CancellationToken.None)).Allow, "Uncertain choice became an allow");
            handler.Invalid = true;
            try { await gate.Decide("Bad response", CancellationToken.None); throw new Exception("Invalid probability accepted"); } catch (InvalidOperationException) { }
            Check(handler.Models == 1, "Shared transport rediscovered its model for every gate call");
        } finally { Environment.SetEnvironmentVariable("JEV_API_KEY", key); Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", offline); }
    }
    static async Task OrganizationAndLateInput() {
        var store = new MemoryStore(); var clock = new TestClock(); var model = new FixtureModel(); var gate = new FixtureGate();
        await using var agent = new NoteAgent(store, model, gate, NullLogger<NoteAgent>.Instance, new AiActivity(store), clock);
        async Task Tick(string id) { clock.Advance(10); agent.Tick([id], CancellationToken.None); await agent.Drain(); }
        store.Add("organize"); store.Speech("organize", "math", "R is the shared capacity; ten flows target R/10.");
        store.Transcripts["organize"][0].NoteStatus = "completed";
        var selected = new NoteSection { Id = "fair", Title = "Fair sharing", UserEdited = true,
            Markdown = "## Fair sharing\n\nUser annotation: R/n; n=10 gives R/10. [math]",
            Points = [new NotePoint { Id = "formula", Text = "User annotation: R/n; n=10 gives R/10. [math]", SourceIds = ["math"] }] };
        var untouched = new NoteSection { Id = "security", Title = "Security", Markdown = "## Security\n\nUser safety note.", UserEdited = true };
        store.Notes["organize"] = new Note { SessionId = "organize", Version = 7, Sections = [selected, untouched], Markdown = NoteSections.Render([selected, untouched]) };
        await agent.Organize("organize", "fair", 7, CancellationToken.None);
        Check(store.Notes["organize"].Version == 8 && store.Notes["organize"].Sections[1].Markdown == untouched.Markdown &&
            store.Notes["organize"].Sections[0].Markdown.Contains("R/n") && store.Notes["organize"].Sections[0].UserEdited && gate.Calls == 0,
            "Explicit section organization lost the formula, another section, protection or called Jev");
        var call = store.Activity.Values.Single();
        Check(call.SectionId == "fair" && call.PromptVersion == NoteAgent.OrganizePromptVersion && call.PromptText!.Contains(NoteAgent.OrganizeInstructions) &&
            call.InputHash!.Length == 64 && call.UsageJson is not null && call.Status == "completed", "Organization prompt/lifecycle was not the actual request");
        try { await agent.Organize("organize", "fair", 7, CancellationToken.None); throw new Exception("Stale organization base accepted"); } catch (InvalidOperationException) { }
        model.BeforeFinish = _ => { store.Notes["organize"].Version = 9; return Task.CompletedTask; };
        try { await agent.Organize("organize", "fair", 8, CancellationToken.None); throw new Exception("Concurrent user version overwritten"); } catch (InvalidOperationException) { }
        Check(store.Notes["organize"].Version == 9 && store.Activity.Values.Any(x => x.Status == "failed"), "Organization conflict was not retained and visible");
        model.BeforeFinish = null;
        store.Add("late"); store.Chunks["late"].Add(new ChunkRecord { Id = "awaiting", Status = "pending-asr" });
        store.Gates["late"] = new NoteGateRecord { SessionId = "late", FlushRequested = true, FlushVersion = 1 };
        await Tick("late"); Check(store.Gates["late"].FlushRequested && gate.Calls == 0, "Stop acknowledged audio before its confirmed ASR arrived");
        store.Chunks["late"][0].Status = "transcribed"; store.Speech("late", "late-final", "This complete example arrived after Stop."); gate.Allow = false;
        await Tick("late"); Check(store.Notes.ContainsKey("late") && !store.Gates["late"].FlushRequested, "Stop failed to include a late final source");
        var count = gate.Calls; await Tick("late"); Check(gate.Calls == count, "Completed Stop input was evaluated again");
        store.Add("deferred"); store.Speech("deferred", "half", "The unresolved sentence ends with");
        store.Gates["deferred"] = new NoteGateRecord { SessionId = "deferred", FlushRequested = true, FlushVersion = 1 }; model.DeferAll = true;
        await Tick("deferred"); count = gate.Calls; await Tick("deferred");
        Check(!store.Notes.ContainsKey("deferred") && store.Transcripts["deferred"][0].NoteStatus == "deferred" && gate.Calls == count,
            "All-deferred output saved an empty version, completed the source or paid for unchanged Stop input again");
        model.DeferAll = false; gate.Allow = true;
        store.Add("mixed-deferral"); store.Speech("mixed-deferral", "written", "A complete source-backed point.");
        store.Speech("mixed-deferral", "continuation", "The next formula begins with"); model.DeferLast = true;
        await agent.Generate("mixed-deferral", CancellationToken.None);
        Check(store.Transcripts["mixed-deferral"][0].NoteStatus == "completed" && store.Transcripts["mixed-deferral"][1].NoteStatus == "deferred" &&
            store.Notes["mixed-deferral"].Coverage.Single(x => x.SourceId == "continuation").Status == "deferred",
            "Mixed written/deferred output left the incomplete source pending or falsely completed it");
        model.DeferLast = false;
        store.Add("render-citations"); store.Speech("render-citations", "cited-source", "An exact supported definition.");
        model.OmitInlineCitations = true; await agent.Generate("render-citations", CancellationToken.None); model.OmitInlineCitations = false;
        Check(store.Transcripts["render-citations"][0].NoteStatus == "completed" &&
            store.Notes["render-citations"].Citations.Single().SourceIds.SequenceEqual(["cited-source"]) &&
            store.Notes["render-citations"].Sections.Single().Markdown.Contains(store.Notes["render-citations"].Sections.Single().Points.Single().Text),
            "Canonical sourceIds were not rendered as durable citations in the actual point body");
        store.Add("backlog");
        for (var i = 0; i < 40; i++) { store.Speech("backlog", "fragment-" + i, "Unresolved fragment " + i); store.Transcripts["backlog"][i].NoteStatus = "deferred"; }
        store.Speech("backlog", "new-topic", "A complete new topic beyond the old unresolved window.");
        await Tick("backlog"); Check(store.Transcripts["backlog"].Last().NoteStatus == "completed" &&
            store.Transcripts["backlog"].Take(32).All(x => x.NoteStatus == "deferred"), "An old deferred window blocked meaningful later input or falsely completed unseen sources");
        store.Add("material"); store.Materials["material"].Add(new Material { Id = new string('c', 32), Name = "Chinese passage", Text = new string('字', 7000) });
        await Tick("material");
        Check(store.Notes["material"].Coverage.Count > 0 && gate.Inputs.Any(x => x.Contains("Chinese passage")) &&
            model.InputLengths.Max() <= 48000, "Material-only / escaped Chinese input was lost or exceeded the resource limit");
        store.Add("oversized"); store.Speech("oversized", "huge", new string('字', 10000)); count = gate.Calls;
        await Tick("oversized");
        Check(gate.Calls == count && !store.Notes.ContainsKey("oversized") && store.Activity.Values.Any(x => x.SessionId == "oversized" && x.Status == "failed"),
            "One unbounded ASR passage was truncated, sent to a provider or failed invisibly");
        Console.WriteLine("Organization/late input: actual independent prompt, selected protected section only, stale/concurrent base rejection, Stop awaits late final, deferred-only no version/duplicate call, material-only escaped Chinese bound, oversized input visible without provider.");
    }
    sealed class TestClock : TimeProvider {
        long ticks; public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero).AddTicks(ticks);
        public void Advance(double seconds) => ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }
    sealed class FixtureGate() : JevNoteGate(new JevTransport(new GateHandler())) {
        public override bool IsConfigured => true; public int Calls; public bool Allow = true, FailNext; public string? BlockSession;
        public readonly ConcurrentBag<string> Inputs = new(); public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<NoteGateDecision> Decide(string state, CancellationToken ct) {
            Interlocked.Increment(ref Calls); Inputs.Add(state);
            if (FailNext) { FailNext = false; throw new TimeoutException("Synthetic provider timeout"); }
            if (BlockSession is not null && state.Contains("slow session")) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return new(Allow, Allow ? .92 : .1, .9, "fixture-gate", "{\"input_tokens\":12}", 1);
        }
    }
    sealed class FixtureModel : GeminiLanguageModel {
        public override bool IsConfigured => true; public int Calls; public bool FailNext, DeferAll, DeferLast, OmitInlineCitations; public Func<string, Task>? BeforeFinish;
        public readonly ConcurrentBag<int> InputLengths = new();
        public override async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null) {
            Interlocked.Increment(ref Calls); if (FailNext) { FailNext = false; throw new TimeoutException("Synthetic generation timeout"); }
            using var doc = JsonDocument.Parse(prompt); var root = doc.RootElement; var pending = root.GetProperty("pending");
            InputLengths.Add(prompt.Length);
            Check(instructions == NoteAgent.InstructionsFor(root.GetProperty("language").GetString()!) + (name == "LectureSectionOrganizer" ? "\n" + NoteAgent.OrganizeInstructions : ""), "Effective note prompt did not reach the generation call");
            var inputs = pending.EnumerateArray().Concat(root.GetProperty("materials").EnumerateArray()).ToList();
            var points = (DeferLast ? inputs.SkipLast(1) : inputs).Select(x => new { text = x.GetProperty("text").GetString() + (OmitInlineCitations ? "" : " [" + x.GetProperty("id").GetString() + "]"),
                sourceIds = new[] { x.GetProperty("id").GetString()! }, retains = Array.Empty<string>() }).ToArray();
            var result = DeferAll ? JsonSerializer.Serialize(new { sections = Array.Empty<object>(), deferred = inputs.Select(x => new { sourceId = x.GetProperty("id").GetString(), reason = "Needs a complete continuation" }) }, JsonOptions) :
                JsonSerializer.Serialize(new { sections = new[] { new { id = "new", baseVersion = 0, title = "Fixture topic", markdown = "## Fixture topic\n\n" + string.Join("\n\n", points.Select(x => x.text)), points } },
                    deferred = (DeferLast ? inputs.TakeLast(1) : []).Select(x => new { sourceId = x.GetProperty("id").GetString(), reason = "Needs a complete continuation" }) }, JsonOptions);
            if (name == "LectureSectionOrganizer") {
                var section = root.GetProperty("editableSections")[0];
                var organized = section.GetProperty("points").EnumerateArray().Select(x => new { text = x.GetProperty("text").GetString(),
                    sourceIds = x.GetProperty("sourceIds").EnumerateArray().Select(s => s.GetString()).ToArray(), retains = new[] { x.GetProperty("id").GetString() } }).ToArray();
                result = JsonSerializer.Serialize(new { sections = new[] { new { id = section.GetProperty("id").GetString(), baseVersion = section.GetProperty("version").GetInt32(),
                    title = "Organized sharing", markdown = "## Organized sharing\n\n" + string.Join("\n\n", organized.Select(x => x.text)), points = organized } }, deferred = Array.Empty<object>() }, JsonOptions);
            }
            onUsage?.Invoke("{\"input_tokens\":20,\"output_tokens\":12}"); onUpdate?.Invoke(result);
            var id = pending.GetArrayLength() > 0 ? pending[0].GetProperty("sourceId").GetString()! : "";
            if (BeforeFinish is not null) await BeforeFinish(id);
            return result;
        }
    }
    sealed class MemoryStore : PlaybackStore {
        public readonly ConcurrentDictionary<string, List<Transcript>> Transcripts = new();
        public readonly ConcurrentDictionary<string, Note> Notes = new(); public readonly ConcurrentDictionary<string, string> Languages = new();
        public readonly ConcurrentDictionary<string, List<Material>> Materials = new(); public readonly ConcurrentDictionary<string, List<ChunkRecord>> Chunks = new();
        public readonly ConcurrentDictionary<string, NoteGateRecord> Gates = new(); public readonly ConcurrentDictionary<string, ActivityRecord> Activity = new();
        public readonly ConcurrentDictionary<string, List<Playback.Api.Terms.TermCandidate>> Terms = new();
        public readonly ConcurrentDictionary<string, List<TermInsight>> Insights = new();
        public readonly List<string[]> SyncReadIds = [];
        public override Task<List<SyncRecord>> ReadSyncRecords(string id, string kind, string[]? ids) {
            SyncReadIds.Add(ids ?? []);
            return Task.FromResult(kind switch {
                "transcript" => Transcripts[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
                "material" => Materials[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
                "insight" => Insights[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
                _ => new List<SyncRecord>()
            });
        }
        public override Task<Playback.Api.Terms.TermCandidate?> ReadSyncTerm(string id, string term) => Task.FromResult(Terms[id].FirstOrDefault(x => x.Text == term));
        static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
        public void Add(string id) { Transcripts[id] = []; Languages[id] = "zh-Hant"; Materials[id] = []; Chunks[id] = []; Terms[id] = []; Insights[id] = []; }
        public void Speech(string id, string key, string text) => Transcripts[id].Add(new Transcript { Id = key, SessionId = id, SourceId = id,
            Original = text, StartMs = Transcripts[id].Count * 8000, EndMs = (Transcripts[id].Count + 1) * 8000 });
        public override Task<SessionView?> Session(string id) {
            Notes.TryGetValue(id, out var note); var copy = note is null ? null : Copy(note);
            return Task.FromResult<SessionView?>(new(id, id, null, DateTime.UtcNow.Date, copy?.Markdown ?? "", copy?.Version ?? 0, 0, false, "zh-Hant",
                Copy(Materials[id]), Copy(Transcripts[id]), Copy(Chunks[id]), Copy(Terms[id]), Copy(Insights[id]), copy, NoteLanguage: Languages[id]));
        }
        public override Task<NoteGateRecord?> NoteGate(string id) => Task.FromResult(Gates.TryGetValue(id, out var gate) ? Copy(gate) : null);
        public override Task SaveNoteGate(NoteGateRecord item) { Gates[item.SessionId] = Copy(item); return Task.CompletedTask; }
        public override Task AcknowledgeNoteFlush(string id, int version) { if (Gates.TryGetValue(id, out var gate) && gate.FlushVersion == version) gate.FlushRequested = false; return Task.CompletedTask; }
        public override Task SaveActivity(ActivityRecord item) { Activity[item.Id] = Copy(item); return Task.CompletedTask; }
        public override Task<List<ActivityRecord>> ActivityHistory(string id) => Task.FromResult(Activity.Values.Where(x => x.SessionId == id).Select(Copy).ToList());
        public override Task MarkNotes(IEnumerable<string> ids, string status, string? error = null) {
            foreach (var transcript in Transcripts.Values.SelectMany(x => x).Where(x => ids.Contains(x.Id))) transcript.NoteStatus = status;
            return Task.CompletedTask;
        }
        public override Task<object> SaveSectionNote(string id, SectionUpdate update, string hash, int basedOnVersion, string language, string author = "agent", List<string>? deleted = null, List<string>? suppressed = null) {
            Notes.TryGetValue(id, out var previous);
            if ((previous?.Version ?? 0) != basedOnVersion || Languages[id] != language) throw new InvalidOperationException("Base changed during generation");
            if (author.StartsWith("agent")) NoteSections.RequireRetention(previous, update, update.Sections.SelectMany(x => x.Points).SelectMany(x => x.SourceIds), allowProtectedReformat: author == "agent organization");
            var coverage = (previous?.Coverage ?? []).Where(x => !update.Coverage.Any(y => y.SourceId == x.SourceId)).Concat(update.Coverage).ToList();
            var note = new Note { SessionId = id, Version = basedOnVersion + 1, BasedOnVersion = basedOnVersion, CreatedAt = DateTime.UtcNow,
                Markdown = NoteSections.Render(update.Sections), Sections = Copy(update.Sections),
                Citations = Copy(update.Citations), Coverage = Copy(coverage), InputHash = hash, Author = author,
                InputTranscriptIds = update.Coverage.Select(x => x.SourceId).ToList(),
                TranscriptIds = update.Sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds).Distinct().ToList(),
                SuppressedSourceIds = suppressed ?? previous?.SuppressedSourceIds.ToList() ?? [], DeletedSectionIds = deleted ?? previous?.DeletedSectionIds.ToList() ?? [] };
            Notes[id] = note; return Task.FromResult<object>(new { note.Version, note.Markdown, note.Author });
        }
    }
    sealed class GateHandler : HttpMessageHandler {
        public int Models; public string? Request; public double Confidence = .9; public bool Invalid;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            if (request.Method == HttpMethod.Get) { Models++; return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[{\"name\":\"fixture-alias\"}]}") }; }
            Request = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                model = "fixture-version", answers = new { update = new { type = "noul", noul = Invalid ? 2 : .95 },
                    action = new { type = "choice", choice = "update", confidence = Confidence, probabilities = new { update = .95, wait = .05 } } },
                usage = new { input_tokens = 12, output_tokens = 3 } })) };
        }
    }
}
