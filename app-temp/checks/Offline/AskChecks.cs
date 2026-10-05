using Playback.Api.Ask;
using Playback.Api.Db;

// Ask: which sources a question retrieves from a long lecture, how short citation aliases expand,
// and which answers count as source-backed.
static class AskChecks
{
    public static void Run()
    {
        var longSession = LectureFixtures.Session(LectureFixtures.ThreeHours());
        var earlyAnswer = ChatContextBuilder.Build(longSession, new QuestionInput("What does spectrogram show?"));
        var lateAnswer = ChatContextBuilder.Build(longSession, new QuestionInput("Explain the kernel?"));
        Expect.That(earlyAnswer.RelevantTranscripts[0].Id == "long-4" &&
            lateAnswer.RelevantTranscripts[0].Id == "long-355" &&
            earlyAnswer.Prompt.Contains("Source [03] (120000-180000 ms)") &&
            lateAnswer.Prompt.Contains("Source [178] (10620000-10680000 ms)"),
            "Three-hour questions must retrieve early and late grouped sources with exact passage ranges");

        var aliases = earlyAnswer.References!;
        Expect.That(aliases.Encode("Point [long-4, long-5]").Contains("[03]") &&
            aliases.Decode("Point [03]") == "Point [long-4, long-5]",
            "Short audio aliases must expand to every underlying chunk in the cited passage");
        Expect.That(aliases.Decode("`[03]`\n```text\n[03]\n```") == "`[03]`\n```text\n[03]\n```",
            "Short aliases inside literal code must not be rewritten");
        Expect.Rejects(() => aliases.Decode("Unknown [9999]"), "An unknown short citation was accepted");

        var widePassage = LectureFixtures.WidestPassage();
        var wideContext = ChatContextBuilder.Build(longSession with { Transcripts = widePassage }, new QuestionInput("Explain the bottleneck"));
        var wideCitation = "Point [" + string.Join(", ", widePassage.Select(x => x.Id)) + "]";
        Expect.That(wideCitation.Length > 2000 && wideContext.References!.Encode(wideCitation) == "Point [01]" &&
            ChatAgent.CitedEvidence(wideContext, wideContext.References.Decode("Point [01]")).Length == 16,
            "The maximum merged passage must retain all long canonical source IDs while using one short model alias");

        var citedEarly = ChatAgent.CitedEvidence(earlyAnswer, "A spectrogram shows frequency over time [long-4].");
        Expect.That(citedEarly.Length == 1 && citedEarly[0].Id == "long-4" &&
            citedEarly[0].Kind == "lecture" && citedEarly[0].Label == "120000-150000 ms",
            "Q&A evidence must link an actual cited transcript to its audio time");
        var uncertainEvidence = ChatAgent.CitedEvidence(earlyAnswer, "The speaker said [unclear] about frequency [long-4].");
        Expect.That(uncertainEvidence.Length == 1 && uncertainEvidence[0].Id == "long-4",
            "ASR uncertainty markers must remain distinct from source citations");
        var focusedEarly = ChatContextBuilder.Build(longSession, new QuestionInput("What does it show?", TranscriptId: "long-4"));
        Expect.Rejects(() => ChatAgent.CitedEvidence(focusedEarly, "A spectrogram shows frequency over time."),
            "Uncited focused source was accepted");
        Expect.Rejects(() => ChatAgent.CitedEvidence(earlyAnswer, "A spectrogram shows frequency over time [invented]."),
            "Invented source was accepted");
        Expect.Rejects(() => ChatAgent.CitedEvidence(earlyAnswer, "A spectrogram shows frequency over time [long-4] [invented]."),
            "Invented extra source was accepted");
        var groupedContext = ChatContextBuilder.Build(longSession, new QuestionInput("Explain kernel and spectrogram"));
        Expect.That(ChatAgent.CitedEvidence(groupedContext, "Both sources [long-4, long-355].").Length == 2,
            "Grouped exact source IDs must validate without accepting invented IDs");

        var materialSession = LectureFixtures.Session([], [new Material { Id = "material-one", Name = "Handout", Text = "FFT means fast Fourier transform." }]);
        var materialContext = ChatContextBuilder.Build(materialSession, new QuestionInput("What does FFT mean?"));
        var citedMaterial = ChatAgent.CitedEvidence(materialContext, "FFT means fast Fourier transform [material-one].");
        Expect.That(citedMaterial.Length == 1 && citedMaterial[0].Kind == "material" && citedMaterial[0].Label == "Handout",
            "Material-only Q&A must link the cited handout");
        var manyMaterials = Enumerable.Range(0, 7).Select(i => new Material
        {
            Id = $"handout-{i}",
            Name = $"Handout {i}",
            Text = i == 6 ? new string('x', 3_200) + " Convolution combines two signals." : "Unrelated practice questions."
        }).ToList();
        var materialSearchSession = materialSession with { Materials = manyMaterials };
        var foundLateMaterial = ChatContextBuilder.Build(materialSearchSession, new QuestionInput("What does convolution combine?"));
        Expect.That(foundLateMaterial.Materials.Any(x => x.Id == "handout-6") &&
            foundLateMaterial.Prompt.Contains("Convolution combines two signals."),
            "Q&A must retrieve a relevant later handout and include the matching passage");
        var focusedMaterial = ChatContextBuilder.Build(materialSearchSession, new QuestionInput("What does convolution combine?", MaterialId: "handout-1"));
        Expect.That(focusedMaterial.Materials[0].Id == "handout-1",
            "An explicitly selected handout must remain first in the Q&A source set");

        var oversizedTranscript = LectureFixtures.Session([new Transcript
        {
            Id = "long-source",
            StartMs = 10_000,
            EndMs = 40_000,
            Original = new string('x', 10_000) + " Plasma oscillations depend on electron density."
        }]);
        var longSourceContext = ChatContextBuilder.Build(oversizedTranscript, new QuestionInput("What affects plasma oscillations?", TranscriptId: "long-source"));
        Expect.That(longSourceContext.Prompt.Contains("Plasma oscillations depend on electron density.") && longSourceContext.Prompt.Length < 5_000,
            "Long transcript questions must include the matching passage in a bounded prompt");

        var chineseSession = LectureFixtures.Session(LectureFixtures.ThreeHoursInChinese());
        var chineseAnswer = ChatContextBuilder.Build(chineseSession, new QuestionInput("傅立葉變換是甚麼？"));
        Expect.That(chineseAnswer.RelevantTranscripts[0].Id == "zh-5" && chineseAnswer.Prompt.Contains("Source [03] (120000-180000 ms)"),
            "Chinese questions must retrieve an early matching source from a long lecture");
        var mixedAnswer = ChatContextBuilder.Build(chineseSession, new QuestionInput("FFT是甚麼？"));
        Expect.That(mixedAnswer.RelevantTranscripts[0].Id == "zh-5",
            "Mixed English and Chinese questions must preserve the English term");

        Console.WriteLine("Ask checks passed: early/late and Chinese retrieval over three hours, alias expansion, cited evidence only, material questions, bounded prompts");
    }
}
