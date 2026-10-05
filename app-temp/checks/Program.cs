// Playback checks. `dotnet run` runs every offline check: in-memory HTTP and storage, no network, no database,
// no audio upload. A flag runs one other group; the comment above each group says what it needs.
try
{
    switch (args)
    {
        case []:
            await RunOffline(notesOnly: false);
            break;
        case ["--notes"]:
            await RunOffline(notesOnly: true);
            break;

        // Offline, reading the Week 3 sample in app-temp/data/test-audio/sampleAudio; decoding the M4A needs Windows.
        case ["--sample-audio", var folder]:
            SampleAudioChecks.SourceLookup(folder);
            break;
        case ["--sample-audio-preview", var folder]:
            SampleAudioChecks.Decode(folder);
            break;
        case ["--vad-sample", var folder]:
            SampleAudioChecks.Vad(folder);
            break;
        case ["--asr-compare"]:
            await AsrComparison.Run(live: false);
            break;

        // MongoDB on localhost, database playback_e2e. Nothing is started; an unavailable database is reported.
        case ["--conversation-check"]:
            await ConversationStoreChecks.Run();
            break;
        case ["--notes-store-check"]:
            await NoteStoreChecks.Run();
            break;
        case ["--session-sync-store-check"]:
            await SessionSyncStoreChecks.Run();
            break;

        // Paid providers. Each refuses to start without its opt-in variable; the Live/run-*.mjs scripts set it.
        case ["--gemini-live"]:
            await GeminiLiveCheck.Run();
            break;
        case ["--jev-live"]:
            await JevLiveCheck.Run();
            break;
        case ["--asr-synthetic-live"]:
            await AsrLiveCheck.Run();
            break;
        case ["--sample-audio-live", var folder]:
            await SampleAudioLiveCheck.Run(folder);
            break;
        case ["--asr-compare", "--live"]:
            await AsrComparison.Run(live: true);
            break;

        // Validation runs: write their evidence into a run folder under app-temp/data/validation.
        case ["--validation-fixtures"]:
            ValidationFixtures.Run();
            break;
        case ["--notes-recovery-preview", var folder]:
            NotesRecoveryPreview.Run(folder);
            break;
        case ["--note-coverage-replay", var folder]:
            await NoteCoverageRuns.Replay(folder, live: false);
            break;
        case ["--note-coverage-live", var folder]:
            await NoteCoverageRuns.Replay(folder, live: true);
            break;
        case ["--note-coverage-store", var folder]:
            await NoteCoverageRuns.Store(folder);
            break;
        case ["--week3-live", var folder]:
            await Week3ProviderRun.Run(folder);
            break;
        case ["--week3-store-seed", var folder]:
            await Week3StoreRun.Seed(folder);
            break;
        case ["--week3-term-live", var folder]:
            await Week3StoreRun.Term(folder);
            break;

        default:
            Console.Error.WriteLine("Unknown check. Run without arguments for the offline checks; Program.cs lists the other groups.");
            Environment.ExitCode = 2;
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(WithoutCredentials(ex.ToString()));
    Environment.ExitCode = 1;
}

// The offline mode also stops NoteAgent's background scan, which would otherwise read MongoDB.
static async Task RunOffline(bool notesOnly)
{
    Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
    if (!notesOnly)
    {
        RecordingChecks.Run();
        await AsrChecks.Run();
        await LiveAsrChecks.Run();
        LanguageChecks.Run();
        TranslationChecks.Run();
        AskChecks.Run();
        await TermChecks.Run();
        await GeminiChecks.Run();
    }
    NoteDocumentChecks.Run();
    await NoteGenerationChecks.Run();
    await NoteSchedulingChecks.Run();
    await NoteCoverageChecks.Run();
    await SessionSyncChecks.Run();
}

// A live check can fail with a provider message; never print a credential.
static string WithoutCredentials(string text)
{
    foreach (var name in new[] { "GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY", "DASHSCOPE_API_KEY" })
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } key) text = text.Replace(key, "[REDACTED]", StringComparison.Ordinal);
    return text;
}
