using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Playback.Application.Activity;
using Playback.Domain;
using Playback.Domain.Models;
using Playback.Infrastructure;
using Playback.Infrastructure.Storage;
using NotePrompts = Playback.Application.Notes.NotePrompts;

// Network: the storage format against the database in PLAYBACK_MONGO_URI (MongoDB Atlas or Firestore), in playback_e2e.
// One synthetic session: a transcript and an AI activity saved through the store must have no null fields and no prompt
// text, read back as before (the null-field filter that finds work included), and are deleted afterwards. Prompt files go
// to a temporary folder. Needs PLAYBACK_MONGO_STORE_LIVE=yes (set by run-mongo-storage-live.mjs).
static class MongoStorageLiveCheck
{
    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_MONGO_STORE_LIVE") != "yes")
            throw new InvalidOperationException("Set PLAYBACK_MONGO_STORE_LIVE=yes for the MongoDB storage check");
        // The database the store itself picks, so the raw reads below look at the same one.
        var uri = PlaybackEnvironment.MongoUri;
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var folder = Path.Combine(Path.GetTempPath(), "playback-store-live-" + Guid.NewGuid().ToString("N"));
        PlaybackEnvironment.ConfigureStorage(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataPath"] = folder }).Build());
        var store = new PlaybackStore();
        if (!await store.IsReady())
            throw new InvalidOperationException("Database unreachable within the app's server selection timeout; nothing was written. " + await Diagnose(uri));
        var raw = new MongoClient(uri).GetDatabase("playback_e2e");
        await store.EnsureRecoveryIndexes(CancellationToken.None);
        var indexes = await (await raw.GetCollection<BsonDocument>("ai_activity").Indexes.ListAsync()).ToListAsync();
        Expect.That(indexes.Any(x => x["name"] == "activity_session_time") && indexes.Any(x => x["name"] == "activity_expiry" &&
            x.GetValue("expireAfterSeconds", BsonNull.Value).ToDouble() == PlaybackStore.ActivityRetention.TotalSeconds),
            "The database did not keep the activity session index and 30-day expiry");
        var sessionId = JsonSerializer.SerializeToElement(await store.CreateSession("Storage format check " + Guid.NewGuid().ToString("N")[..8]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("id").GetString()!;
        string sizes;
        try
        {
            var hash = ContentHash.Of("synthetic audio");
            var chunk = new ChunkRecord { Id = ChunkIdentity.Id(sessionId, "microphone", 1, hash), SessionId = sessionId,
                SourceId = "microphone", Sequence = 1, StartMs = 0, EndMs = 8_000, Hash = hash };
            await store.SaveTranscript(chunk, "Fourier transform 係將訊號拆成唔同頻率。");
            var transcript = await raw.GetCollection<BsonDocument>("transcripts").Find(new BsonDocument("_id", chunk.Id)).FirstAsync();
            Expect.That(!transcript.Elements.Any(x => x.Value.IsBsonNull), "A saved transcript still has null fields: " +
                string.Join(", ", transcript.Elements.Where(x => x.Value.IsBsonNull).Select(x => x.Name)));
            Expect.That(await store.TranscriptForChunk(chunk.Id) is { Active: true, Translation: null, Original: "Fourier transform 係將訊號拆成唔同頻率。" },
                "A saved transcript does not read back as before");
            Expect.That((await store.SessionsWithPendingNotes()).Contains(sessionId),
                "The database does not match a missing SupersededBy as null, so new transcripts would get no notes");

            var instructions = NotePrompts.SegmentInstructions("zh-Hant");
            var activity = new AiActivity(store);
            var call = await activity.Begin(sessionId, "Storage check", "fixture", "fixture-model", [chunk.Id]);
            activity.Context(call, "storage-check-v1", instructions, "synthetic input");
            await activity.Start(call);
            await activity.End(call, "completed", "Saved");
            var saved = await raw.GetCollection<BsonDocument>("ai_activity").Find(new BsonDocument("_id", call.Id)).FirstAsync();
            Expect.That(!saved.Contains("PromptText") && saved["PromptHash"] == call.PromptHash && !saved.Elements.Any(x => x.Value.IsBsonNull),
                "A saved activity still has its prompt text or null fields");
            Expect.That(File.Exists(Path.Combine(folder, "prompts", call.PromptHash + ".txt")), "The activity prompt was not filed");
            Expect.That(await new PlaybackStore().ActivityHistory(sessionId) is [{ PromptText: var text, Status: "completed" }] && text == instructions,
                "A new store instance does not read the filed prompt back");

            await store.SaveNote(sessionId, "## Fourier\n\n訊號可以拆成頻率。", "user");
            await store.SaveNote(sessionId, "## Fourier\n\n訊號可以拆成頻率。\n\nFFT 將計算由 N² 減到 N log N。", "user", 1);
            var noteDocuments = await raw.GetCollection<BsonDocument>("notes").Find(new BsonDocument("SessionId", sessionId)).ToListAsync();
            var log = await new NoteCommands(new NoteStoreAdapter(store), new NoteStoreAdapter(store)).EditLog(sessionId);
            Expect.That(noteDocuments.Count == 2 && noteDocuments.All(x => !x.Contains("Edits")) &&
                log.Single(x => x.Version == 2).Edits is [{ Kind: "insert" }, ..] edits && edits.Any(x => x.Text.Contains("FFT")),
                "Note versions still store edits, or the edit log lost them");

            // A finished chunk becomes Opus: the WAV goes, the record says opus, readers still get a WAV of the same length,
            // and a retried upload of the same audio does not bring the WAV back.
            var source = Path.Combine(folder, "upload.wav");
            Directory.CreateDirectory(folder);
            var pcm = Enumerable.Range(0, 16_000 * 8).Select(i => (short)(5000 * Math.Sin(2 * Math.PI * 220 * i / 16_000.0) * Math.Max(0, Math.Sin(Math.PI * 4 * i / 16_000.0)))).ToArray();
            using (var writer = new NAudio.Wave.WaveFileWriter(source, new NAudio.Wave.WaveFormat(16_000, 16, 1))) writer.WriteSamples(pcm, 0, pcm.Length);
            var audio = await store.SaveLocalChunk(sessionId, "system", 2, 8_000, 16_000, source, CancellationToken.None);
            await store.SaveTranscript(audio, "");
            var wavBytes = new FileInfo(audio.Path).Length;
            Expect.That((await store.CompressibleChunks(DateTime.UtcNow.AddMinutes(1), 1000)).Any(x => x.Id == audio.Id) && await store.CompressChunk(audio),
                "A finished chunk was not offered for compression or did not compress");
            var opusFile = Path.ChangeExtension(audio.Path, ".opus");
            var storedChunk = await raw.GetCollection<BsonDocument>("chunks").Find(new BsonDocument("_id", audio.Id)).FirstAsync();
            Expect.That(!File.Exists(audio.Path) && File.Exists(opusFile) && storedChunk["Storage"] == "opus" &&
                !(await store.CompressibleChunks(DateTime.UtcNow.AddMinutes(1), 1000)).Any(x => x.Id == audio.Id),
                "Compression left the WAV, lost the Opus file or did not record it");
            Expect.That(store.ChunkAudioPath(audio.Id) is { } readBack && new FileInfo(readBack).Length == wavBytes &&
                (await store.Chunk(audio.Id))?.Path == readBack, "A compressed chunk does not read back as a WAV of the same length");
            await store.SaveLocalChunk(sessionId, "system", 2, 8_000, 16_000, source, CancellationToken.None);
            Expect.That(!File.Exists(audio.Path), "A retried upload brought the WAV back next to its Opus copy");
            var audioSizes = $"chunk audio {wavBytes} → {new FileInfo(opusFile).Length} B";

            sizes = $"{audioSizes}, transcript {Before(transcript, typeof(Transcript)).ToBson().Length} → {transcript.ToBson().Length} B, " +
                $"activity {Before(saved, typeof(ActivityRecord)).Set("PromptText", instructions).ToBson().Length} → {saved.ToBson().Length} B";
        }
        finally
        {
            await store.DeleteSession(sessionId);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        var left = 0L;
        foreach (var name in new[] { "transcripts", "ai_activity" })
            left += await raw.GetCollection<BsonDocument>(name).CountDocumentsAsync(new BsonDocument("SessionId", sessionId));
        left += await raw.GetCollection<BsonDocument>("sessions").CountDocumentsAsync(new BsonDocument("_id", sessionId));
        Expect.That(left == 0, "The synthetic session left documents in playback_e2e");
        Console.WriteLine($"MongoDB storage live check passed (playback_e2e): activity session index and 30-day expiry kept, note versions without stored edits and an edit log worked out on read, a finished chunk kept as Opus and read back as WAV, no null fields, missing field matched as null, prompt filed " +
            $"and read by a new store, synthetic session deleted. Per document, old format → new: {sizes}");
    }

    // A slower ping tells a short timeout from a refused connection (network access list, credentials, DNS).
    static async Task<string> Diagnose(string uri)
    {
        var settings = MongoClientSettings.FromConnectionString(uri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(20);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await new MongoClient(settings).GetDatabase("playback_e2e").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            return $"A 20 s ping succeeded after {watch.ElapsedMilliseconds} ms, so only the app's timeout was too short.";
        }
        catch (Exception ex) { return $"A 20 s ping failed too after {watch.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}"; }
    }

    // The same document as the format before this change wrote it: every mapped member, nulls included.
    static BsonDocument Before(BsonDocument saved, Type type)
    {
        var before = saved.DeepClone().AsBsonDocument;
        foreach (var member in BsonClassMap.LookupClassMap(type).AllMemberMaps)
            if (!before.Contains(member.ElementName)) before.Add(member.ElementName, BsonNull.Value);
        return before;
    }
}
