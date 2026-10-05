using Playback.Api.Db;
using Playback.Api;
using Playback.Api.Notes;
using Playback.Api.Ask;
using Playback.Api.Translation;

// Language settings: spoken and output languages stay distinct, Cantonese is shown in Traditional
// script without changing the saved ASR text, and the settings reach the prompts.
static class LanguageChecks
{
    public static void Run()
    {
        foreach (var language in new[] { "auto", "yue-en", "yue", "zh", "en" }) LanguageSettings.ValidateAsr(language);
        foreach (var language in new[] { "zh-Hant", "zh-Hans", "en" }) LanguageSettings.ValidateNote(language);
        Expect.Rejects(() => LanguageSettings.ValidateAsr("zh-Hant"), "ASR accepted a writing system as a spoken language");
        Expect.Rejects(() => LanguageSettings.ValidateNote("yue"), "Notes accepted an unsupported output language");
        Expect.That(NoteInstructions.For("zh-Hant").Contains("Traditional Chinese") &&
            NoteInstructions.For("zh-Hans").Contains("Simplified Chinese") &&
            NoteInstructions.For("en").Contains("Write section Markdown and point text in English"),
            "Note output settings must reach the generation instructions");
        Expect.That(TranslationAgent.Instructions("yue-Hant").Contains("natural written Cantonese") &&
            TranslationAgent.Instructions("zh-Hans").Contains("Simplified Chinese"),
            "Translations must distinguish Cantonese from standard written Chinese and character script");
        const string raw = "广东话，电脑，学习，FFT，我哋喺度學嘢。";
        const string expected = "廣東話，電腦，學習，FFT，我哋喺度學嘢。";
        if (OperatingSystem.IsWindows())
        {
            var display = LanguageSettings.CantoneseDisplay(raw, "yue", null);
            Expect.That(LanguageSettings.CantoneseDisplay(raw, "yue-en", null) == expected && LanguageSettings.ProviderHint("yue-en") is null, "Mixed language mode must preserve English and use automatic recognition");
            Expect.That(display == expected && LanguageSettings.CantoneseDisplay(raw, "auto", "Cantonese") == expected,
                $"Cantonese display must convert script while retaining Cantonese and English words: {display}");
            var transcript = new Transcript { Id = "cantonese", Original = raw, DisplayOriginal = display };
            Expect.That(transcript.Original == raw && transcript.SourceText == expected, "Script conversion must preserve raw ASR text");
            var session = new SessionView("fixture", "Language fixture", null, DateTime.UtcNow, "", 0, 0, false,
                "zh-Hant", [], [transcript], [], [], [], null);
            var context = ChatContextBuilder.Build(session, new QuestionInput("What was said?", TranscriptId: "cantonese", SelectedText: "電腦"));
            Expect.That(context.Prompt.Contains("電腦"), "Selecting converted text must retain source-backed Q&A");
        }
        Expect.That(LanguageSettings.CantoneseDisplay(raw, "zh", "Chinese") == raw &&
            LanguageSettings.CantoneseDisplay("Hello FFT", "auto", "en") == "Hello FFT",
            "Non-Cantonese text must retain its provider output");
        Console.WriteLine("Language checks passed: distinct spoken/output languages, Cantonese script, raw-text preservation and source selection");
    }
}
