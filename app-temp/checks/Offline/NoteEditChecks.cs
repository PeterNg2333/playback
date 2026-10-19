using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Application.Notes;

// Notes change like an editor applying edits: added content applies at once, Jev decides text edits, larger
// deletions wait for a person, and one refused change never blocks the rest of the reply.
static class NoteEditChecks
{
    const string Id = "edits";

    public static async Task Run()
    {
        var store = new MemoryStore(); store.Add(Id);
        var writer = new ScriptedWriter();
        var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
        var commands = new NoteCommands(notes, notes, notes);

        // New content through operations.
        const string s1 = "TCP uses a three-way handshake: SYN, SYN-ACK, ACK.";
        store.Speech(Id, "s1", s1);
        writer.Next = input => Ops(
            new() { Kind = "create-topic", TopicId = "new:tcp", Title = "TCP" },
            new() { Kind = "add-block", TopicId = "new:tcp", BlockId = "new:handshake", Role = "definition", Body = "TCP 以三次握手建立連線：SYN、SYN-ACK、ACK。", SourceIds = [Alias(input, s1)] });
        await workflow.Generate(Id, default);
        var tcp = store.Notes[Id].Sections.Single(); var handshake = tcp.Blocks.Single();

        // An old-style reply that sends the existing topic with only a new point appends it; one bad operation,
        // a placeholder, a duplicate topic title and an empty topic do not stop the rest.
        const string s2 = "The handshake keeps old duplicate connections from being accepted.";
        store.Speech(Id, "s2", s2);
        writer.Next = input => new NotePatch
        {
            Sections = [new() { Id = tcp.Id, Title = "TCP", Points = [
                new() { Text = "握手可以避免接受舊的重複連線。", SourceIds = [Alias(input, s2)] },
                new() { Text = "TCP 用三次握手。", SourceIds = [Alias(input, s1)] }] }],
            Operations = [
                new() { Kind = "add-block", TopicId = "section_missing", BlockId = "new:lost", Role = "detail", Body = "Lost [x]", SourceIds = [Alias(input, s2)] },
                new() { Kind = "add-block", TopicId = tcp.Id, BlockId = "new:placeholder", Role = "detail", Body = "...", SourceIds = [Alias(input, s2)] },
                new() { Kind = "create-topic", TopicId = "new:again", Title = "tcp" },
                new() { Kind = "add-block", TopicId = "new:again", BlockId = "new:duplicate-check", Role = "detail", Body = "重複連線會被拒絕。", SourceIds = [Alias(input, s2)] },
                new() { Kind = "create-topic", TopicId = "new:empty", Title = "Empty" }]
        };
        await workflow.Generate(Id, default);
        var second = store.Notes[Id]; var topic = second.Sections.Single();
        Expect.That(LastRevision(store).Status == "completed" && second.Version == 2, "A reply with one bad change was not applied");
        Expect.That(topic.Id == tcp.Id && topic.Blocks.Single(x => x.Id == handshake.Id).Body == handshake.Body,
            "Appending changed or replaced the earlier block");
        Expect.That(topic.Blocks.Count == 3 && topic.Blocks.Any(x => x.Body.Contains("舊的重複連線")) && topic.Blocks.Any(x => x.Body.Contains("會被拒絕")) &&
            topic.Blocks.All(x => !x.Body.StartsWith("...")) && !topic.Blocks.Any(x => x.Body.StartsWith("TCP 用三次握手")),
            "Append, duplicate-title merge, placeholder rejection or restatement drop failed");
        Expect.That(store.Transcripts[Id].Single(x => x.Id == "s2").NoteStatus == "completed", "Appended speech was not marked written");
        var appended = topic.Blocks.Single(x => x.Body.Contains("舊的重複連線")); var merged = topic.Blocks.Single(x => x.Body.Contains("會被拒絕"));

        // A small edit that keeps every fact applies on its own; a larger one becomes a suggestion; new content still applies.
        const string s3 = "Correction: the handshake also synchronises the sequence numbers.";
        store.Speech(Id, "s3", s3);
        var longer = string.Concat(Enumerable.Repeat("較早或重複的連線請求", 31));
        writer.Next = input => Ops(
            new() { Kind = "replace-text", BlockId = handshake.Id, Find = "建立連線", Replace = "建立連線並同步序號", Reason = "s3 adds sequence numbers" },
            new() { Kind = "replace-text", BlockId = appended.Id, Find = "舊的重複連線", Replace = longer, Reason = "more detail" },
            new() { Kind = "add-block", TopicId = tcp.Id, BlockId = "new:sequence", Role = "detail", Body = "握手同時同步雙方序號。", SourceIds = [Alias(input, s3)] });
        await workflow.Generate(Id, default);
        topic = store.Notes[Id].Sections.Single();
        var edited = topic.Blocks.Single(x => x.Id == handshake.Id);
        Expect.That(edited.Body.Contains("建立連線並同步序號") && edited.CitationIds.SequenceEqual(handshake.CitationIds) &&
            edited.Anchors.Single().Length == edited.Body.Length, "A small fact-keeping edit was not applied with its citation and coverage kept");
        Expect.That(topic.Blocks.Single(x => x.Id == appended.Id).Body == appended.Body, "A large edit changed the text without review");
        var shorter = store.RewriteProposals.Values.Single();
        Expect.That(shorter.Changes.Single() is { Kind: "replace", TargetBlockId: var target } && target == appended.Id && shorter.Changes[0].Reason.Contains("more than 300"),
            "A large edit was not kept as a suggestion");
        Expect.That(topic.Blocks.Any(x => x.Body.Contains("同步雙方序號")) && LastRevision(store).Summary!.Contains("1 edits applied automatically"),
            "Edits were not decided one by one or new content was blocked");
        var sequence = topic.Blocks.Single(x => x.Body.Contains("同步雙方序號"));

        // A deletion that keeps every source cited elsewhere applies; one that would lose a source's last
        // citation waits for a person.
        const string s4 = "Both sides then start sending data within a second.";
        store.Speech(Id, "s4", s4);
        writer.Next = input => Ops(
            new() { Kind = "delete-block", BlockId = merged.Id, Reason = "duplicate of the appended block" },
            new() { Kind = "delete-block", BlockId = sequence.Id, Reason = "already said" },
            new() { Kind = "add-block", TopicId = tcp.Id, BlockId = "new:data", Role = "detail", Body = "握手完成後 1 秒內雙方開始傳送資料。", SourceIds = [Alias(input, s4)] });
        await workflow.Generate(Id, default);
        topic = store.Notes[Id].Sections.Single();
        Expect.That(topic.Blocks.All(x => x.Id != merged.Id) && topic.Blocks.Any(x => x.Id == sequence.Id),
            "A safe deletion was not applied, or a last-citation deletion skipped review");
        var data = topic.Blocks.Single(x => x.Body.Contains("傳送資料"));
        var deletion = store.RewriteProposals.Values.Single(x => x.Changes[0].Kind == "delete");
        Expect.That(deletion.Changes[0].TargetBlockId == sequence.Id && deletion.Changes[0].Reason.Contains("last citation"), "A last-citation deletion was not suggested");

        // More deletions than the automatic limit all wait for a person.
        const string s5 = "Closing the connection uses FIN packets.";
        store.Speech(Id, "s5", s5);
        writer.Next = input => Ops(topic.Blocks.Take(3).Select(x => new NoteTreeOperation { Kind = "delete-block", BlockId = x.Id, Reason = "shorter" })
            .Append(new() { Kind = "add-block", TopicId = tcp.Id, BlockId = "new:fin", Role = "detail", Body = "關閉連線使用 FIN 封包。", SourceIds = [Alias(input, s5)] }).ToArray());
        var before = store.Notes[Id].Sections.Single().Blocks.Select(x => x.Id).ToList();
        await workflow.Generate(Id, default);
        Expect.That(before.All(id => store.Notes[Id].Sections.Single().Blocks.Any(x => x.Id == id)) &&
            store.RewriteProposals.Values.Count(x => x.Changes[0].Kind == "delete" && x.Changes[0].Reason.Contains("3 deletions")) == 3,
            "A large deletion applied without a person");

        // An edit that drops a number waits for a person; a protected block is never edited.
        const string s6 = "FIN is acknowledged by the other side.";
        store.Speech(Id, "s6", s6);
        var current = store.Notes[Id];
        await commands.EditBlock(Id, current.Version, handshake.Id, current.Sections.Single().Blocks.Single(x => x.Id == handshake.Id).Body + " 使用者補充。");
        writer.Next = input => Ops(
            new() { Kind = "replace-text", BlockId = handshake.Id, Find = "使用者補充", Replace = "AI 改寫", Reason = "protected" },
            new() { Kind = "replace-text", BlockId = data.Id, Find = "1 秒內", Replace = "很快", Reason = "simpler" },
            new() { Kind = "replace-text", BlockId = appended.Id, Find = "避免", Replace = string.Concat(Enumerable.Repeat("防止", 151)), Reason = "wording" },
            new() { Kind = "add-block", TopicId = tcp.Id, BlockId = "new:ack", Role = "detail", Body = "對方會確認 FIN。", SourceIds = [Alias(input, s6)] });
        var suggestions = store.RewriteProposals.Count;
        await workflow.Generate(Id, default);
        topic = store.Notes[Id].Sections.Single();
        Expect.That(topic.Blocks.Single(x => x.Id == handshake.Id).Body.Contains("使用者補充") && topic.Blocks.Single(x => x.Id == appended.Id).Body == appended.Body &&
            topic.Blocks.Single(x => x.Id == data.Id).Body.Contains("1 秒內") && store.RewriteProposals.Count == suggestions + 2 &&
            store.RewriteProposals.Values.Any(x => x.Changes[0].Reason.Contains("would drop")) && LastRevision(store).Summary!.Contains("Protected block"),
            "A protected block was edited, or an edit dropping a number applied without review");

        // Replies in the shapes real models send still apply: an alias without "new:", content written into
        // create-topic, and source citations written as [ref:T001]; an unknown term reference is still refused.
        const string s7 = "The four-way close sends FIN and ACK in both directions.";
        var handshakeCitation = handshake.CitationIds.Single();
        store.Speech(Id, "s7", s7);
        writer.Next = input => Ops(
            new() { Kind = "create-topic", TopicId = "closing", Title = "Closing", Body = $"四次揮手雙向傳送 FIN 與 ACK。[ref:{Alias(input, s7)}]", SourceIds = [Alias(input, s7)] },
            new() { Kind = "add-block", TopicId = "closing", BlockId = "close-detail", Role = "detail", Body = $"雙方各自關閉自己的方向。[{handshakeCitation}]", SourceIds = [Alias(input, s7)] },
            new() { Kind = "add-block", TopicId = "closing", BlockId = "bad-ref", Role = "detail", Body = "見 [ref:unknown-term]。", SourceIds = [Alias(input, s7)] });
        await workflow.Generate(Id, default);
        var closing = store.Notes[Id].Sections.Single(x => x.Title == "Closing");
        Expect.That(closing.Blocks.Count == 2 && closing.Blocks.Any(x => x.Body.Contains("四次揮手") && !x.Body.Contains("ref:")) &&
            closing.Blocks.All(x => !x.CitationIds.Contains(handshakeCitation)) &&
            store.Transcripts[Id].Single(x => x.Id == "s7").NoteStatus == "completed" && LastRevision(store).Summary!.Contains("unknown term reference"),
            "A reply with a plain alias, topic content or [ref:] source citations was refused, a copied earlier citation was kept, or an unknown term reference was kept");

        // A suggestion stays valid while the notes grow and goes stale only when its own block changes.
        await commands.AcceptRewrite(Id, shorter.Id, true);
        Expect.That(store.Notes[Id].Sections.Single(x => x.Id == tcp.Id).Blocks.Single(x => x.Id == appended.Id).Body.Contains(longer),
            "A suggestion was refused after unrelated new versions");
        await commands.AcceptRewrite(Id, deletion.Id, true);
        Expect.That(store.Notes[Id].Sections.Single(x => x.Id == tcp.Id).Blocks.All(x => x.Id != sequence.Id), "An accepted deletion did not remove the block");
        var wording = store.RewriteProposals.Values.Single(x => x.Changes[0].Kind == "replace" && x.Changes[0].Body.Contains("防止防止"));
        await Expect.RejectsAsync(() => commands.AcceptRewrite(Id, wording.Id, true), "A suggestion for a block that has since changed was applied");
        await InputLayout();
        await OwnerActions();
        await ClosedTopics();
        await Placement();
        await TraditionalScript();
        Console.WriteLine("Note edit checks passed: required placement (outline and blocks, nested new topics, no closed topic offered), Simplified-only characters converted in Traditional notes (shared characters kept), closed topics (full or long-running) move new speech into a new subtopic, owner instructions and block comments applied directly, locked blocks untouched, dismissed suggestions hidden, lecture-first cached input with stable aliases, compact Jev input, one deferral per source, topics in lecture order, append without copying, real reply shapes (plain aliases, topic content, [ref:] citations), per-change rejection, placeholder/duplicate/empty topics, rule-applied and suggested edits, last-citation and large deletions for review, protected blocks, suggestions valid until their block changes");
    }

    static ActivityRecord LastRevision(MemoryStore store) =>
        store.Activity.Values.Where(x => x.Task == "Note revision").OrderBy(x => x.StartedAt).Last();

    // The writer reads the lecture first, in time order and unchanged from call to call, so providers can cache
    // it; Jev reads only the passage and the speech before it; a source is deferred at most once; and a topic
    // written later from earlier speech takes its place in lecture order.
    static async Task InputLayout()
    {
        const string id = "layout";
        var store = new MemoryStore(); store.Add(id);
        var writer = new ScriptedWriter(); var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
        const string a = "今日講字幕檔的時間軸。", b = "Subtitle timing uses start and end times.", c = "Each cue has an index.";
        store.Speech(id, "a", a); store.Speech(id, "b", b);
        writer.Next = input => new NotePatch
        {
            Operations = [new() { Kind = "create-topic", TopicId = "timing", Title = "Timing" },
                new() { Kind = "add-block", TopicId = "timing", BlockId = "times", Role = "detail", Body = "每段字幕有開始及結束時間。", SourceIds = [Alias(input, b)] }],
            Deferred = [new() { SourceId = Alias(input, a), Reason = "the sentence continues" }]
        };
        await workflow.Generate(id, default);
        store.Speech(id, "c", c);
        writer.Next = input => Ops(
            new() { Kind = "create-topic", TopicId = "intro", Title = "Introduction", Order = 9 },
            new() { Kind = "add-block", TopicId = "intro", BlockId = "intro-text", Role = "detail", Body = "本節講字幕檔的時間軸。", SourceIds = [Alias(input, a)] },
            new() { Kind = "add-block", TopicId = store.Notes[id].Sections.Single().Id, BlockId = "index", Role = "detail", Body = "每段字幕有編號。", SourceIds = [Alias(input, c)] });
        await workflow.Generate(id, default);
        string LecturePrefix(string input) => input[..input.IndexOf("]],\"omittedEarlierSources\"", StringComparison.Ordinal)];
        Expect.That(writer.Inputs[1].StartsWith(LecturePrefix(writer.Inputs[0]), StringComparison.Ordinal) && writer.Inputs[0].StartsWith("{\"language\"") &&
            writer.Inputs[0].Contains(a) && Alias(Json(writer.Inputs[0]), b) == Alias(Json(writer.Inputs[1]), b),
            "The lecture was not the stable, unescaped first part of the writer input");
        var second = Json(writer.Inputs[1]);
        var deferredBefore = second.GetProperty("pending").EnumerateArray().Single(x => x.GetProperty("id").GetString() == Alias(second, a));
        var deferrable = ((Microsoft.Extensions.AI.ChatResponseFormatJson)Playback.Api.Providers.ModelOutputContract.ResponseFormat("LectureSectionWriter", writer.Inputs[1])!)
            .Schema!.Value.GetProperty("properties").GetProperty("deferred").GetProperty("items").GetProperty("properties").GetProperty("sourceId").GetProperty("enum")
            .EnumerateArray().Select(x => x.GetString()).ToList();
        Expect.That(deferredBefore.GetProperty("deferredBefore").GetBoolean() && !deferrable.Contains(Alias(second, a)) && deferrable.Contains(Alias(second, c)),
            "A source deferred once could be deferred again");
        var outline = NoteTree.Outline(store.Notes[id].Sections).Select(x => x.Topic.Title).ToList();
        Expect.That(outline.SequenceEqual(["Introduction", "Timing"]), "A topic written later from earlier speech was not placed in lecture order");

        // A repeated deferral is ignored: the source stays pending instead of waiting again.
        const string d = "The format is plain text.", e = "A blank line separates cues.";
        store.Speech(id, "d", d);
        writer.Next = input => new NotePatch { Deferred = [new() { SourceId = Alias(input, d), Reason = "continues" }] };
        await workflow.Generate(id, default);
        store.Speech(id, "e", e);
        writer.Next = input => new NotePatch
        {
            Operations = [new() { Kind = "add-block", TopicId = store.Notes[id].Sections.Single(x => x.Title == "Timing").Id, BlockId = "blank", Role = "detail",
                Body = "字幕之間以空行分隔。", SourceIds = [Alias(input, e)] }],
            Deferred = [new() { SourceId = Alias(input, d), Reason = "continues again" }]
        };
        await workflow.Generate(id, default);
        Expect.That(store.Transcripts[id].Single(x => x.Id == "d").NoteStatus == "pending" && LastRevision(store).Summary!.Contains("1 repeated deferrals ignored"),
            "A second deferral of the same source was accepted");

        var session = (await notes.Session(id))!;
        var gate = Json(Playback.Application.Notes.NoteInput.Gate(session, Playback.Application.Notes.NoteInput.Pending(session), [], "automatic").Text);
        Expect.That(!gate.TryGetProperty("lecture", out _) && gate.GetProperty("before").GetArrayLength() <= 6 &&
            gate.GetProperty("pending").EnumerateArray().Any(x => x.GetProperty("text").GetString() == d), "Jev did not get the compact passage input");
    }

    // What the note owner does: lock a block, give an instruction for a topic or a comment on one block (applied
    // directly, larger edits and deletions included, but never to a locked block), and dismiss a suggestion.
    static async Task OwnerActions()
    {
        const string id = "owner";
        var store = new MemoryStore(); store.Add(id);
        var writer = new ScriptedWriter(); var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
        var commands = new NoteCommands(notes, notes, notes);
        const string o1 = "Paging maps virtual pages to frames.", o2 = "A page fault loads a missing page.";
        store.Speech(id, "o1", o1); store.Speech(id, "o2", o2);
        writer.Next = input => Ops(
            new() { Kind = "create-topic", TopicId = "paging", Title = "Paging" },
            new() { Kind = "add-block", TopicId = "paging", BlockId = "map", Role = "definition", Body = "分頁把虛擬頁對應到實體頁框。", SourceIds = [Alias(input, o1)] },
            new() { Kind = "add-block", TopicId = "paging", BlockId = "fault", Role = "detail", Body = "缺頁時會載入缺少的頁。", SourceIds = [Alias(input, o2)] });
        await workflow.Generate(id, default);
        var topic = store.Notes[id].Sections.Single(); var map = topic.Blocks[0]; var fault = topic.Blocks[1];
        await commands.LockBlock(id, store.Notes[id].Version, map.Id, true);
        Expect.That(store.Notes[id].Sections.Single().Blocks.Single(x => x.Id == map.Id).UserEdited && store.Notes[id].Author == "user lock", "A block lock was not saved");

        var longer = string.Concat(Enumerable.Repeat("缺頁中斷會暫停程式並由作業系統載入資料。", 20));
        writer.Next = input => Ops(
            new() { Kind = "replace-text", BlockId = map.Id, Find = "分頁", Replace = "Paging 分頁", Reason = "instruction" },
            new() { Kind = "replace-text", BlockId = fault.Id, Find = "缺頁時會載入缺少的頁。", Replace = longer, Reason = "instruction" });
        var result = await workflow.Instruct(id, "Explain the page fault paragraph in more detail", topic.Id, null, store.Notes[id].Version, default);
        var blocks = store.Notes[id].Sections.Single().Blocks;
        Expect.That(result.Applied == 1 && result.NotApplied.Any(x => x.Contains("Protected block")) && blocks.Single(x => x.Id == map.Id).Body == map.Body &&
            blocks.Single(x => x.Id == fault.Id).Body.Contains(longer) && store.Notes[id].Author == "user instruction" &&
            store.Activity.Values.Any(x => x.Task == "Note instruction" && x.Status == "completed"),
            "An instruction was not applied directly, or it changed a locked block");

        writer.Next = input => Ops(new NoteTreeOperation { Kind = "delete-block", BlockId = fault.Id, Reason = "the owner asked" });
        result = await workflow.Instruct(id, "Remove this paragraph", null, fault.Id, store.Notes[id].Version, default);
        Expect.That(result.Applied == 1 && store.Notes[id].Sections.Single().Blocks.All(x => x.Id != fault.Id), "A comment on a block did not apply its deletion");
        await Expect.RejectsAsync(() => workflow.Instruct(id, "Anything", null, null, store.Notes[id].Version - 1, default), "A stale instruction was applied");

        await commands.LockBlock(id, store.Notes[id].Version, map.Id, false);
        const string o3 = "Each process has its own page table.";
        store.Speech(id, "o3", o3);
        writer.Next = input => Ops(
            new() { Kind = "replace-text", BlockId = map.Id, Find = "分頁", Replace = string.Concat(Enumerable.Repeat("分頁", 160)), Reason = "too long" },
            new() { Kind = "add-block", TopicId = topic.Id, BlockId = "table", Role = "detail", Body = "每個行程有自己的頁表。", SourceIds = [Alias(input, o3)] });
        await workflow.Generate(id, default);
        var suggestion = (await commands.Rewrites(id)).Single();
        await commands.DismissRewrite(id, suggestion.Id);
        Expect.That((await commands.Rewrites(id)).Count == 0 && store.RewriteProposals[suggestion.Id].Status == "dismissed",
            "An unlocked block's large edit was not suggested, or a dismissed suggestion was still listed");
    }

    // A topic that is full (six blocks) or has run eight minutes of lecture takes no new blocks: the input marks it
    // closed, a block still aimed at it goes into a new subtopic titled by its first phrase (nothing is refused),
    // and a subtopic the model makes itself under the closed topic is fine.
    static async Task ClosedTopics()
    {
        const string id = "closed";
        var store = new MemoryStore(); store.Add(id);
        var writer = new ScriptedWriter(); var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
        var facts = Enumerable.Range(0, 6).Select(i => $"Scheduling fact {i}.").ToArray();
        for (var i = 0; i < 6; i++) store.Speech(id, "f" + i, facts[i]);
        writer.Next = input => Ops([new() { Kind = "create-topic", TopicId = "sched", Title = "Scheduling" },
            .. facts.Select((fact, i) => new NoteTreeOperation { Kind = "add-block", TopicId = "sched", BlockId = "b" + i, Role = "detail", Body = $"排程要點 {i}。", SourceIds = [Alias(input, fact)] })]);
        await workflow.Generate(id, default);
        var topic = store.Notes[id].Sections.Single();
        const string more = "Round robin uses a time slice.", next = "Priority scheduling picks the highest priority.";
        store.Speech(id, "more", more);
        writer.Next = input => Ops(new NoteTreeOperation { Kind = "add-block", TopicId = topic.Id, BlockId = "rr", Role = "detail", Body = "輪轉排程使用時間片。", SourceIds = [Alias(input, more)] });
        await workflow.Generate(id, default);
        var closedInOutline = Json(writer.Inputs[^1]).GetProperty("topicOutline").EnumerateArray().Single(x => x.GetProperty("id").GetString() == topic.Id).GetProperty("closed").GetBoolean();
        var split = store.Notes[id].Sections.SingleOrDefault(x => x.ParentId == topic.Id);
        Expect.That(closedInOutline && store.Notes[id].Sections.Single(x => x.Id == topic.Id).Blocks.Count == 6 && split?.Title == "輪轉排程使用時間片" &&
            split.Blocks.Count == 1 && store.Transcripts[id].Single(x => x.Id == "more").NoteStatus == "completed" &&
            LastRevision(store).Summary!.Contains("1 blocks moved from closed topics"), "A full topic took a new block, or the block was not moved into a new subtopic");
        store.Speech(id, "next", next);
        writer.Next = input => Ops(
            new() { Kind = "create-topic", TopicId = "priority", Title = "Priority scheduling", ParentId = topic.Id },
            new() { Kind = "add-block", TopicId = "priority", BlockId = "prio", Role = "detail", Body = "優先排程選最高優先者。", SourceIds = [Alias(input, next)] });
        await workflow.Generate(id, default);
        var sections = store.Notes[id].Sections;
        Expect.That(sections.Single(x => x.Id == topic.Id).Blocks.Count == 6 && sections.Any(x => x.ParentId == topic.Id && x.Title == "Priority scheduling") &&
            store.Transcripts[id].All(x => x.NoteStatus == "completed"), "A subtopic the model made under a closed topic was refused");

        var session = (await notes.Session(id))!;
        var renamed = NoteTree.Apply(store.Notes[id].Sections, [new() { Kind = "rename-topic", TopicId = split!.Id, Title = "Round robin" }],
            session.AllowedSources, "rename", structuralOnly: true);
        Expect.That(renamed.Single(x => x.Id == split.Id).Title == "Round robin", "The organizer could not rename an automatically titled topic");
        var old = new NoteSection { Id = "old", Title = "Old", SchemaVersion = 2, Blocks = [new() { Id = "ob", Body = "x", Anchors = [new() { Id = "oa", Length = 1, SourceIds = ["f0"] }] }] };
        session.Sections.Add(old);
        var late = new NoteTranscript { Id = "late", StartMs = NoteStructure.MaxTopicMinutes * 60_000L + 1, SourceText = "later speech" };
        Expect.That(Playback.Application.Notes.NoteInput.ClosedTopics(session, [late]).Contains("old") &&
            !Playback.Application.Notes.NoteInput.ClosedTopics(session, [new NoteTranscript { Id = "soon", StartMs = 60_000, SourceText = "soon" }]).Contains("old"),
            "A topic running for many minutes was not closed, or a short one was");
    }

    // Placement is a required decision: outline declares new topics with their parents (a new topic may sit under
    // another new one), every block names its topic, and the schema offers no closed topic and no add-block operation.
    static async Task Placement()
    {
        const string id = "placement";
        var store = new MemoryStore(); store.Add(id);
        var writer = new ScriptedWriter(); var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
        const string a = "Paging maps pages to frames.", b = "A page fault loads the page.";
        store.Speech(id, "a", a); store.Speech(id, "b", b);
        writer.Next = input => new NotePatch
        {
            Outline = [new() { Key = "new2", Title = "缺頁", Parent = "new1" }, new() { Key = "new1", Title = "分頁", Parent = "" }],
            Blocks = [new() { Topic = "new1", Role = "definition", Body = "分頁把頁對應到頁框。", SourceIds = [Alias(input, a)] },
                new() { Topic = "new2", Role = "detail", Body = "缺頁時載入該頁。", SourceIds = [Alias(input, b)] }]
        };
        await workflow.Generate(id, default);
        var sections = store.Notes[id].Sections;
        var paging = sections.Single(x => x.Title == "分頁"); var fault = sections.Single(x => x.Title == "缺頁");
        Expect.That(paging.ParentId is null && fault.ParentId == paging.Id && paging.Blocks.Count == 1 && fault.Blocks.Count == 1,
            "Declared topics or their parent were not created, or blocks went elsewhere");

        for (var i = 0; i < 5; i++) { store.Speech(id, "p" + i, "Paging detail " + i); }
        writer.Next = input => new NotePatch { Blocks = Enumerable.Range(0, 5).Select(i => new PlacedBlock { Topic = paging.Id, Role = "detail",
            Body = $"分頁細節 {i}。", SourceIds = [Alias(input, "Paging detail " + i)] }).ToList() };
        await workflow.Generate(id, default);
        store.Speech(id, "c", "TLB caches translations.");
        writer.Next = _ => new NotePatch();
        try { await workflow.Generate(id, default); } catch (InvalidOperationException) { } // an empty reply leaves the source pending
        var schema = ((Microsoft.Extensions.AI.ChatResponseFormatJson)Playback.Api.Providers.ModelOutputContract.ResponseFormat("LectureSectionWriter", writer.Inputs[^1])!)
            .Schema!.Value.GetProperty("properties");
        var topics = schema.GetProperty("blocks").GetProperty("items").GetProperty("properties").GetProperty("topic").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
        var kinds = schema.GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
        Expect.That(!topics.Contains(paging.Id) && topics.Contains(fault.Id) && topics.Contains("new1") && !kinds.Contains("add-block") && !kinds.Contains("create-topic") &&
            schema.GetProperty("outline").GetProperty("items").GetProperty("properties").GetProperty("parent").GetProperty("enum").EnumerateArray().Any(x => x.GetString() == paging.Id),
            "The writer schema offered a closed topic, or allowed placement outside outline and blocks");
        var operation = schema.GetProperty("operations").GetProperty("items");
        Expect.That(operation.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["kind", "topicId", "blockId"]) &&
            operation.GetProperty("properties").GetProperty("topicId").GetProperty("enum").EnumerateArray().Any(x => x.GetString() == "new1"),
            "An operation could leave out its target topic or block");

        // Speech ten minutes later may not open a subtopic under paging: it is no longer being discussed.
        store.Transcripts[id].Single(x => x.Id == "c").StartMs = 10 * 60_000;
        try { await workflow.Generate(id, default); } catch (InvalidOperationException) { }
        var later = ((Microsoft.Extensions.AI.ChatResponseFormatJson)Playback.Api.Providers.ModelOutputContract.ResponseFormat("LectureSectionWriter", writer.Inputs[^1])!)
            .Schema!.Value.GetProperty("properties").GetProperty("outline").GetProperty("items").GetProperty("properties").GetProperty("parent")
            .GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
        Expect.That(!later.Contains(paging.Id) && !later.Contains(fault.Id) && later.Contains(""),
            "A topic spoken long before the new speech was offered as a parent");
    }

    // Traditional notes: Simplified-only characters the model lets in become Traditional (Hong Kong); characters that
    // are also correct Traditional stay, so 干擾, 皇后 and 公里 are untouched; find is not converted; other scripts are left alone.
    static async Task TraditionalScript()
    {
        var script = new OpenccNoteScript();
        Expect.That(script.Apply("讲师从预告：干擾、皇后、公里、了解、着重、台灣、游泳、茶几", "zh-Hant") == "講師從預告：干擾、皇后、公里、了解、着重、台灣、游泳、茶几" &&
            script.Apply("这里", "zh-Hant") == "這裡" && script.Apply("讲师", "zh-Hans") == "讲师" && script.Apply("網絡協定", "zh-Hant") == "網絡協定",
            "OpenCC changed a correct Traditional character, missed a Simplified-only one, or touched Simplified notes");
        const string id = "script";
        var store = new MemoryStore(); store.Add(id); store.Languages[id] = "zh-Hant";
        var writer = new ScriptedWriter(); var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes, script: script);
        store.Speech(id, "a", "The lecturer previews the demo.");
        writer.Next = input => new NotePatch
        {
            Outline = [new() { Key = "new1", Title = "讲师预告", Parent = "" }],
            Blocks = [new() { Topic = "new1", Role = "detail", Body = "讲师从这里开始，价格万一变动，避免干擾。", SourceIds = [Alias(input, "The lecturer previews the demo.")] }]
        };
        await workflow.Generate(id, default);
        var topic = store.Notes[id].Sections.Single();
        Expect.That(topic.Title == "講師預告" && topic.Blocks.Single().Body.StartsWith("講師從這裡開始，價格萬一變動，避免干擾。"),
            "A Traditional note kept Simplified characters from the model, or converted a correct Traditional word");
    }

    static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }

    static NotePatch Ops(params NoteTreeOperation[] operations) => new() { Operations = operations.ToList() };

    // The input calls each source by a short alias; find it by the source's text in the lecture rows
    // [id, source, start, text, uncertain].
    static string Alias(JsonElement input, string text) =>
        input.GetProperty("lecture").EnumerateArray().First(x => x[3].GetString() == text)[0].GetString()!;

    sealed class ScriptedWriter : INoteWriter
    {
        public Func<JsonElement, NotePatch> Next = _ => new();
        public readonly List<string> Inputs = [];
        public bool IsConfigured => true;
        public string Provider => "fixture";
        public string Model => "scripted-edits";
        public Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            Inputs.Add(input);
            using var document = JsonDocument.Parse(input);
            return Task.FromResult(Next(document.RootElement.Clone()));
        }
    }
}
