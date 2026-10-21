using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Playback.Api.Activity;
using Playback.Api.Ask;
using Playback.Api.Audio.Asr;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Terms;
using Playback.Api.Translation;
using Playback.Application.Workspace;
using Playback.Infrastructure;

static class MigrationChecks
{
    public static async Task Run()
    {
        var domain = typeof(Transcript).Assembly; var application = typeof(ChatAgent).Assembly; var infrastructure = typeof(PlaybackStore).Assembly;
        Expect.That(domain == typeof(Note).Assembly && application == typeof(NoteWorkflow).Assembly && infrastructure != typeof(Playback.Api.Endpoints.HealthEndpoints).Assembly, "Feature implementation still lives in the API host");
        Expect.That(domain.GetTypes().All(t => t.GetCustomAttributesData().Concat(t.GetProperties().SelectMany(p => p.GetCustomAttributesData())).All(a => !a.AttributeType.FullName!.StartsWith("MongoDB"))), "Domain documents retain persistence attributes");
        Playback.Infrastructure.Storage.DocumentMappings.Register();
        Expect.That(!new ChunkRecord {Path = "private-path"}.ToBsonDocument().Contains("Path") && !new ActivityRecord {Draft = "live-only"}.ToBsonDocument().Contains("Draft"), "Migration changed ignored BSON fields");
        var session = BsonSerializer.Deserialize<SessionRecord>(new BsonDocument {{"_id", "legacy"}, {"Title", "Original"}, {"unknownHistoricalField", true}});
        Expect.That(session.Id == "legacy" && session.Title == "Original", "Legacy session ignore-extra reader changed");
        Expect.That(new DemoUser {Username = "owner"}.ToBsonDocument()["_id"] == "owner", "User owner identity changed");
        var services = new ServiceCollection(); services.AddLogging(); services.AddHttpContextAccessor(); services.AddPlayback();
        services.AddSingleton<PlaybackStore>(new MemoryStore());
        services.AddSingleton<TranslationAgent>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions {ValidateOnBuild = true, ValidateScopes = true});
        foreach (var type in new[] {typeof(NoteCommands), typeof(NoteWorkflow), typeof(ChatAgent), typeof(TermReview), typeof(WorkspaceCommands), typeof(Playback.Application.Recording.RecordingCommands), typeof(Playback.Application.Recording.ChunkCommands), typeof(RuntimeQueries)})
            _ = provider.GetRequiredService(type);
        var workers = provider.GetServices<IHostedService>().ToList();
        // The independent migration commit also builds with the preserved translation WIP left unstaged.
        if ((object)provider.GetRequiredService<TranslationAgent>() is IHostedService translation) workers.Add(translation);
        Expect.That(workers.Count is 8 or 9 && workers.Any(x => x is ConceptGraphWorker) && workers.Any(x => x is TranscriptReviewWorker) && workers.Any(x => x is SessionRerunWorker) && workers.All(x => x is BackgroundService), "Processing workers do not share a hosted lifecycle");
        foreach (var worker in workers) await worker.StartAsync(default);
        foreach (var worker in workers.AsEnumerable().Reverse()) await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("Migration checks passed: model/feature assemblies, persistence-free domain, legacy BSON contracts/owner IDs, complete service composition, hosted start/cancellation/shutdown (offline; no devices/database/providers)");
    }
}
