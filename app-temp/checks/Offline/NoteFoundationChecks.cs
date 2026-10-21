using System.Text.Json;

// Executable dependency and mutation boundaries, not folder-name assertions.
static class NoteFoundationChecks
{
    public static void Run()
    {
        var domain = typeof(NoteMutation).Assembly;
        var application = typeof(NoteWorkflow).Assembly;
        var infrastructure = typeof(Playback.Api.Notes.NoteAgent).Assembly;
        Expect.That(domain.GetReferencedAssemblies().All(x => x.Name!.StartsWith("System") || x.Name == "netstandard"),
            "Domain gained a framework, persistence, provider or host dependency");
        Expect.That(application.GetReferencedAssemblies().All(x => x.Name!.StartsWith("System") || x.Name is "netstandard" or "Playback.Domain"),
            "Application gained an infrastructure or API dependency");
        Expect.That(infrastructure.GetReferencedAssemblies().All(x => x.Name != "Playback.Api"), "Infrastructure references the host");
        Expect.That(typeof(Note).GetProperties().All(x => x.GetCustomAttributesData().All(a => !a.AttributeType.FullName!.StartsWith("MongoDB"))),
            "Domain note has persistence attributes");

        var old = new Note { SessionId = "s", Version = 1, Markdown = "Point [a]",
            Coverage = [new SourceDisposition { SourceId = "a", Status = "covered", PointIds = ["p"] }] };
        var before = JsonSerializer.Serialize(old);
        var update = NoteMutation.Prepare("s", new SectionUpdate([], [], []));
        var next = NoteMutation.Create("s", old, update, "edit", 1, "en", "en", "user");
        Expect.That(next.Coverage.Single().Status == "suppressed" && JsonSerializer.Serialize(old) == before,
            "Creating a new revision changed the previous revision's coverage");
        Expect.Rejects(() => NoteMutation.Create("s", old, update, "stale", 0, "en", "en", "user"), "Mutation accepted a stale note version");
        Expect.Rejects(() => NoteMutation.Create("s", old, update, "language", 1, "en", "zh-Hant", "user"), "Mutation accepted a stale language snapshot");
        Console.WriteLine("Note foundation checks passed: independent Domain, inward dependencies, persistence-free models, immutable prior coverage, stale version/language rejected");
    }
}
