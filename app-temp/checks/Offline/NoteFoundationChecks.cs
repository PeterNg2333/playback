using System.Text.Json;

// Executable dependency and mutation boundaries, not folder-name assertions.
static class NoteFoundationChecks
{
    public static void Run()
    {
        var domain = typeof(NoteMutation).Assembly;
        var application = typeof(NoteWorkflow).Assembly;
        var infrastructure = typeof(NoteAgent).Assembly;
        Expect.That(domain.GetReferencedAssemblies().All(x => x.Name!.StartsWith("System") || x.Name == "netstandard"),
            "Domain gained a framework, persistence, provider or host dependency");
        Expect.That(application.GetReferencedAssemblies().All(x => x.Name!.StartsWith("System") || x.Name is "netstandard" or "Playback.Domain"),
            "Application gained an infrastructure or API dependency");
        Expect.That(infrastructure.GetReferencedAssemblies().All(x => x.Name != "Playback.Api"), "Infrastructure references the host");
        Expect.That(typeof(Note).GetProperties().All(x => x.GetCustomAttributesData().All(a => !a.AttributeType.FullName!.StartsWith("MongoDB"))),
            "Domain note has persistence attributes");

        var old = new Note { SessionId = "s", Version = 1, Markdown = "## 1 Topic\n\nPoint [a]",
            Sections = [new NoteSection { Id = "t", Title = "Topic", Blocks = [new NoteBlock { Id = "b", Body = "Point [a]", SourceIds = ["a"] }] }] };
        var before = JsonSerializer.Serialize(old);
        var edited = NoteTree.Clone(old.Sections[0]);
        edited.Blocks[0].Body = "Point, edited [a]";
        var update = NoteMutation.Prepare("s", new SectionUpdate([edited], []));
        var next = NoteMutation.Create("s", old, update, "edit", 1, "en", "en", "user");
        Expect.That(JsonSerializer.Serialize(old) == before && next.Version == 2 && next.Sections[0].Blocks[0].Body.StartsWith("Point, edited [cite_")
            && next.Citations.Single().SourceIds.SequenceEqual(["a"]) && next.TranscriptIds.SequenceEqual(["a"]),
            "A new version must compact citations and list its sources without changing the previous version");
        Expect.Rejects(() => NoteMutation.Create("s", old, update, "stale", 0, "en", "en", "user"), "Mutation accepted a stale note version");
        Expect.Rejects(() => NoteMutation.Create("s", old, update, "language", 1, "en", "zh-Hant", "user"), "Mutation accepted a stale language snapshot");
        Console.WriteLine("Note foundation checks passed: independent Domain, inward dependencies, persistence-free models, immutable previous version, stale version/language rejected");
        MarkdownEdit();
    }

    // The owner saves the whole notes as Markdown: unchanged blocks keep their identity, new text becomes locked blocks.
    static void MarkdownEdit()
    {
        var topics = new List<NoteSection>
        {
            new() { Id = "t1", Title = "Caching", Blocks = [new() { Id = "a", Order = 0, Body = "Keeps hot data. [s1]", SourceIds = ["s1"] },
                new() { Id = "b", Order = 1, Body = "Costs memory. [s2]", SourceIds = ["s2"] }] },
            new() { Id = "t2", Title = "Eviction", ParentId = "t1", Blocks = [new() { Id = "c", Body = "LRU drops the oldest. [s3]", SourceIds = ["s3"] }] },
            new() { Id = "t3", Title = "Removed", Blocks = [new() { Id = "d", Body = "Gone. [s4]", SourceIds = ["s4"] }] }
        };
        var note = new Note { SessionId = "s", Version = 1, Sections = topics, Markdown = NoteTree.Render(topics) };
        Expect.That(note.Markdown.StartsWith("## 1 Caching") && note.Markdown.Contains("### 1.1 Eviction"), "Rendered headings lost their numbering");
        var allowed = new[] { "s1", "s2", "s3", "s4", "s5" };
        Expect.That(NoteSections.UserEdit(note, "s", note.Markdown, allowed, []).Select(x => x.Id).SequenceEqual(["t1", "t2", "t3"]),
            "Saving unchanged Markdown changed the topics");

        var markdown = note.Markdown.Replace("Costs memory. [s2]", "Costs memory. [s2]\n\nA new owner note. [s5]")
            .Replace("### 1.1 Eviction", "### 1.1 Eviction policies").Replace("## 2 Removed\n\nGone. [s4]", "");
        var citations = new List<NoteCitation>();
        var edited = NoteSections.UserEdit(note, "s", markdown, allowed, citations);
        var caching = edited.Single(x => x.Id == "t1");
        var added = caching.Blocks[2];
        Expect.That(edited.Count == 2 && caching.Blocks.Select(x => x.Id).Take(2).SequenceEqual(["a", "b"]) && !caching.Blocks[0].UserEdited
            && added.UserEdited && added.SourceIds.SequenceEqual(["s5"]) && added.Body.Contains("[cite_") && citations.Count == 1,
            "A Markdown save must keep unchanged blocks and add new text as a locked, cited block");
        var eviction = edited.Single(x => x.Id == "t2");
        Expect.That(eviction.Title == "Eviction policies" && eviction.UserEdited && eviction.ParentId == "t1" && eviction.Blocks.Single().Id == "c",
            "A renamed subtopic must keep its identity, parent and blocks and keep the owner's title");
        Expect.Rejects(() => NoteSections.UserEdit(note, "s", "### Orphan\n\nText", allowed, []), "A subtopic without a parent was accepted");
        Console.WriteLine("Markdown edit checks passed: unchanged save keeps topics, unchanged blocks keep their identity, new text becomes a locked cited block, renamed subtopic keeps its parent, orphan subtopic rejected");
    }
}
