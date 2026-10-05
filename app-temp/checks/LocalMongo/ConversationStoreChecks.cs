using Playback.Api.Db;
using Playback.Api.Activity;
using Playback.Api.Ask;
using Playback.Api.Providers;
using System.Text.Json;

// Saved Ask conversations in local MongoDB (playback_e2e): kept per session, replayed after a restart without
// a second answer, and an honest "insufficient" answer offline. Removes its synthetic sessions afterwards.
static class ConversationStoreChecks
{
    public static async Task Run()
    {
        var mongoUri = Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI");
        if (mongoUri is not null && !(mongoUri.StartsWith("mongodb://127.0.0.1:", StringComparison.Ordinal) || mongoUri.StartsWith("mongodb://localhost:", StringComparison.Ordinal)))
            throw new InvalidOperationException("This offline check requires a localhost MongoDB");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) throw new InvalidOperationException("Local MongoDB is unavailable");
        var sessions = new List<string>();
        static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        try {
            foreach (var title in new[] { "E2E demo conversation A", "E2E demo conversation B" })
                sessions.Add(Json(await store.CreateSession(title)).GetProperty("id").GetString()!);
            var first = await store.CreateConversation(sessions[0]);
            var other = await store.CreateConversation(sessions[0]);
            Expect.That((await store.Conversations(sessions[0])).Count == 2 && (await store.Conversations(sessions[1])).Count == 0,
                "Conversation lists must remain session-scoped");
            try { await store.RequireConversation(sessions[1], first.Id); throw new CheckFailed("Cross-session conversation was accepted"); }
            catch (InvalidOperationException) { }
            var activity = new AiActivity(store);
            var model = new GeminiLanguageModel();
            var chat = new ChatAgent(store, model, activity);
            var request = new QuestionInput("What is established by this empty lecture?", RequestId: Guid.NewGuid().ToString(), ConversationId: first.Id);
            var result = Json(await chat.Ask(sessions[0], request, CancellationToken.None));
            Expect.That(result.GetProperty("lectureStatus").GetString() == "insufficient" && result.GetProperty("evidence").GetArrayLength() == 0,
                "Empty lectures must honestly return insufficient evidence without an external request");
            // A fresh agent proves saved replay survives loss of the in-memory request cache.
            var restarted = new ChatAgent(store, model, activity);
            var replay = Json(await restarted.Ask(sessions[0], request, CancellationToken.None));
            Expect.That(replay.GetProperty("questionId").GetString() == result.GetProperty("questionId").GetString() &&
                  (await store.ConversationTurns(sessions[0], first.Id)).Count == 1,
                "Persisted request replay must not generate duplicate conversation turns");
            try { await new ChatAgent(store, model, activity).Ask(sessions[0], request with { Question = "Different payload" }, CancellationToken.None);
                throw new CheckFailed("A reused request ID with a different payload was accepted"); }
            catch (InvalidOperationException) { }
            await chat.Ask(sessions[0], request with { Question = "What remains uncertain?", RequestId = Guid.NewGuid().ToString() }, CancellationToken.None);
            var snapshot = Json(await store.Conversation(sessions[0], first.Id));
            Expect.That(snapshot.GetProperty("turns").GetArrayLength() == 2 && snapshot.GetProperty("title").GetString() == request.Question &&
                  (await store.ConversationTurns(sessions[0], other.Id)).Count == 0,
                "New conversations must have independent messages and retain the first question as their title");
            try { await chat.Ask(sessions[1], request with { RequestId = Guid.NewGuid().ToString() }, CancellationToken.None);
                throw new CheckFailed("Cross-session question was accepted"); }
            catch (InvalidOperationException) { }
            Console.WriteLine("MongoDB conversation checks passed: persistence, session isolation, independent threads, restart replay, payload identity and honest offline answers.");
        } finally { foreach (var id in sessions) await store.DeleteTestSession(id); }
    }
}
