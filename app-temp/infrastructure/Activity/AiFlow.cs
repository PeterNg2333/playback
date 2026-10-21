using Playback.Api.Db;
using Playback.Application.Notes;
using Playback.Api.Notes;
using Playback.Api.Ask;
using Playback.Api.Terms;
using Playback.Api.Translation;
using Playback.Api.Providers;
using Playback.Api.Audio.Asr;
using Playback.Domain.Models;

namespace Playback.Api.Activity;

public sealed record FlowAgent(string Id, string Name, string Provider, string Model, string Status, string PromptVersion,
    string Prompt, string Trigger, string Lifecycle, string InputRole, string OutputRole, List<string> DependsOn);
public sealed class AiFlow(PlaybackStore store, GeminiLanguageModel gemini, Playback.Application.Notes.INoteWriter noteWriter, IJevTransport jev, IAsrAdapter asr, AiActivity activity)
{
    public List<FlowAgent> Agents(SessionRecord? session) {
        var language = session?.NoteLanguage ?? "zh-Hant";
        var offline = PlaybackEnvironment.Offline;
        string WriterStatus(bool enabled = true) => !enabled ? "disabled" : offline ? "offline" : noteWriter.IsConfigured ? "configured" : "not configured";
        string GeminiStatus(bool enabled = true) => !enabled ? "disabled" : offline ? "offline" : gemini.IsConfigured ? "configured" : "not configured";
        return [
            new("asr", "ASR", asr.Model.Provider, session?.AsrModel ?? asr.Model.Model,
                offline || PlaybackEnvironment.ExternalAsrPaused ? "disabled" : "configured", AsrProcessor.PromptVersion,
                AsrProcessor.Instructions + "\nInterim: " + LiveAsrSession.PreviewPromptVersion + "\n" + LiveAsrSession.PreviewInstructions,
                "Saved audio chunks; live interim is preview only", "At most 2 ASR requests; saved chunk identity deduplicates retries; failures remain visible; cancellation/stop flushes capture",
                "Session/source/sequence/hash audio; language hint: " + (session?.AsrLanguage ?? "auto"), "Immutable original ASR + separate display/translation", []),
            new("jot", "Live jot", noteWriter.Provider, noteWriter.Model, WriterStatus(PlaybackEnvironment.AutomaticNotes),
                NotePrompts.Version, NotePrompts.JotInstructions(language), "Each finished minute while recording",
                "Best effort; replaced by the segment of its five minutes", "The last minute and the three before it", "1–4 cited points in the live topic", ["asr"]),
            new("segment", "Note segment", noteWriter.Provider, noteWriter.Model, WriterStatus(PlaybackEnvironment.AutomaticNotes),
                NotePrompts.Version, NotePrompts.SegmentInstructions(language), "Each finished five minutes; again when its transcript changes",
                "One window at a time; failures back off; the transcript hash decides when a window is written again", "The window, the previous segment and the course materials",
                "Cited blocks, concepts and recognition corrections", ["asr"]),
            new("finish", "Outline and polish", noteWriter.Provider, noteWriter.Model, WriterStatus(PlaybackEnvironment.AutomaticNotes),
                NotePrompts.Version, NotePrompts.OutlineInstructions(language) + "\n" + NotePrompts.PolishInstructions(language),
                "After Stop, Revise with AI or a rebuild; a rewritten segment polishes only the topics it fed",
                "Edited and locked blocks keep their text; a polish that drops citations or text keeps the segment blocks", "Every segment block",
                "A topic tree with summary and review questions", ["segment"]),
            new("terms", "Term ranking", "typesafe", "GET /v1/models discovery", offline ? "offline" : jev.IsConfigured && PlaybackEnvironment.AutomaticTerms ? "configured" : "disabled",
                "term-rank-v1", System.Text.Json.JsonSerializer.Serialize(JevTermClassifier.Questions), "Confirmed transcript/material candidates; automatic source changes",
                "Task-specific semaphore; bounded 512-entry/1h cache; contextual identity; ordinary candidates excluded; failure retries bounded",
                "Source-backed candidate + context", "Rank/probability/confidence; no generated summary", ["asr"]),
            new("explanation", "Term explanation", "vertex / Google Search", gemini.Model, GeminiStatus(),
                GeminiLanguageModel.ExplanationPromptVersion, GeminiLanguageModel.ExplanationInstructions, "Jev-selected candidate; explicit detail upgrade for older saved explanations",
                "Saved short/detail reused; hover and render only read saved content; verifiable web sources required; failure does not save success",
                "Term, lecture context, output language", "Saved AI/web supplement + short preview + full explanation + web provenance", ["terms"]),
            new("translation", "Translation", "vertex / Agent Framework", gemini.Model, GeminiStatus(session?.TranslationEnabled == true),
                "translation-v1", TranslationContext.Instructions(session?.TranslationLanguage ?? "zh-Hant"), "15s scan of enabled pending originals; language changes requeue",
                "Bounded 10-target batch; aliases validated; translations versioned separately; original unchanged; retry/backoff; language-safe merge",
                "Original targets + nearby same-session context", "Separate translations, uncertainty preserved", ["asr"]),
            new("chat", "Ask Playback", "vertex / Agent Framework; optional Google Search", gemini.Model, GeminiStatus(),
                ChatAgent.PromptVersion, ChatAgent.Instructions, "Explicit question, selected lecture only",
                "Request identity + bounded saved context/cache; cancelled switch cannot replace another session; exact evidence validation; visible failure/insufficient source; private reasoning not exposed",
                "Question + retrieved session sources; prior turns are context only", "Source-backed answer + distinct web supplement + saved conversation", ["asr", "segment"])
        ];
    }
    public async Task<object> Read(string groupId, string? sessionId) {
        var group = (await store.Groups()).SingleOrDefault(x => x.Id == groupId) ?? throw new InvalidOperationException("Group not found");
        var sessions = (await store.Sessions()).Where(x => x.GroupId == groupId).ToList();
        if (sessionId is not null && !sessions.Any(x => x.Id == sessionId)) throw new InvalidOperationException("Session is not in this group");
        var selected = sessions.FirstOrDefault(x => x.Id == sessionId) ?? sessions.FirstOrDefault();
        return new { group, sessions = sessions.Select(x => new { x.Id, x.Title }), selectedSessionId = selected?.Id,
            agents = Agents(selected), executions = selected is null ? [] : await activity.Read(selected.Id) };
    }
}
