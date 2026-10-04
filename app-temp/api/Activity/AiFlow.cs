using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Ask;
using Playback.Api.Terms;
using Playback.Api.Translation;
using Playback.Api.Providers;
using Playback.Api.Audio.Asr;

namespace Playback.Api.Activity;

public sealed record FlowAgent(string Id, string Name, string Provider, string Model, string Status, string PromptVersion,
    string Prompt, string Trigger, string Lifecycle, string InputRole, string OutputRole, List<string> DependsOn);
public sealed class AiFlow(PlaybackStore store, GeminiLanguageModel gemini, JevNoteGate gate, IAsrAdapter asr, AiActivity activity)
{
    public List<FlowAgent> Agents(SessionRecord? session) {
        var offline = PlaybackEnvironment.Offline;
        string GeminiStatus(bool enabled = true) => !enabled ? "disabled" : offline ? "offline" : gemini.IsConfigured ? "configured" : "not configured";
        return [
            new("asr", "ASR", asr.Model.Provider, session?.AsrModel ?? asr.Model.Model,
                offline || PlaybackEnvironment.ExternalAsrPaused ? "disabled" : "configured", AsrProcessor.PromptVersion,
                AsrProcessor.Instructions + "\nInterim: " + LiveAsrSession.PreviewPromptVersion + "\n" + LiveAsrSession.PreviewInstructions,
                "Saved audio chunks; live interim is preview only", "At most 2 ASR requests; saved chunk identity deduplicates retries; failures remain visible; cancellation/stop flushes capture",
                "Session/source/sequence/hash audio; language hint: " + (session?.AsrLanguage ?? "auto"), "Immutable original ASR + separate display/translation", []),
            new("gate", "Jev note gate", "typesafe", "GET /v1/models discovery", offline ? "offline" : gate.IsConfigured ? "configured" : "not configured",
                JevNoteGate.PromptVersion, System.Text.Json.JsonSerializer.Serialize(JevNoteGate.Questions),
                "10-second checks with changed confirmed input; stop flush; no idle provider call",
                "Per-session dedup; 4 admitted sessions; 30s transport timeout; persisted wait/allow/error; bounded retry; explicit backlog/stop safeguard is labelled separately from Jev allow",
                "Bounded pending passages and section context", "Structured note-specific allow/wait + probability/confidence/usage", ["asr"]),
            new("notes", "Semantic sections & notes", "vertex / Agent Framework", gemini.Model, GeminiStatus(PlaybackEnvironment.AutomaticNotes),
                NoteInstructions.Version, NoteInstructions.For(session?.NoteLanguage ?? "zh-Hant"),
                "Jev allow immediately; labelled stop/backlog safeguard; explicit Revise",
                "Section patches; untouched sections retained; points/provenance checked; deferred sources stay pending; note/language base conflict rejects save; user edits protected; bounded input/output",
                "Confirmed speech + editable section points + same-session materials", "One versioned note with semantic sections and persistent citations", ["gate"]),
            new("organize", "Section organization", "vertex / Agent Framework", gemini.Model,
                GeminiStatus(), NoteInstructions.OrganizeVersion, NoteInstructions.For(session?.NoteLanguage ?? "zh-Hant") + "\n" + NoteInstructions.Organize,
                "Explicit selected section; optional PLAYBACK_AUTO_ORGANIZE=yes scan every 30 minutes for long/repetitive new sections",
                "Same document/history; one section; note/section base validation; retained points; at most one eligible automatic section per session per scan; disabled automatic policy by default",
                "Selected section and its actual sources", "A reversible note version with only that section updated", ["notes"]),
            new("terms", "Term ranking", "typesafe", "GET /v1/models discovery", offline ? "offline" : gate.IsConfigured && PlaybackEnvironment.AutomaticTerms ? "configured" : "disabled",
                "term-rank-v1", System.Text.Json.JsonSerializer.Serialize(JevTermClassifier.Questions), "Confirmed transcript/material candidates; automatic source changes",
                "Task-specific semaphore; bounded 512-entry/1h cache; contextual identity; ordinary candidates excluded; failure retries bounded",
                "Source-backed candidate + context", "Rank/probability/confidence; no generated summary", ["asr"]),
            new("explanation", "Term explanation", "vertex / Google Search", gemini.Model, GeminiStatus(),
                GeminiLanguageModel.ExplanationPromptVersion, GeminiLanguageModel.ExplanationInstructions, "Jev-selected candidate; explicit detail upgrade for older saved explanations",
                "Saved short/detail reused; hover and render only read saved content; verifiable web sources required; failure does not save success",
                "Term, lecture context, output language", "Saved AI/web supplement + short preview + full explanation + web provenance", ["terms"]),
            new("translation", "Translation", "vertex / Agent Framework", gemini.Model, GeminiStatus(session?.TranslationEnabled == true),
                "translation-v1", TranslationAgent.Instructions(session?.TranslationLanguage ?? "zh-Hant"), "15s scan of enabled pending originals; language changes requeue",
                "Bounded 10-target batch; aliases validated; translations versioned separately; original unchanged; retry/backoff; language-safe merge",
                "Original targets + nearby same-session context", "Separate translations, uncertainty preserved", ["asr"]),
            new("chat", "Ask Playback", "vertex / Agent Framework; optional Google Search", gemini.Model, GeminiStatus(),
                ChatAgent.PromptVersion, ChatAgent.Instructions, "Explicit question, selected lecture only",
                "Request identity + bounded saved context/cache; cancelled switch cannot replace another session; exact evidence validation; visible failure/insufficient source; private reasoning not exposed",
                "Question + retrieved session sources; prior turns are context only", "Source-backed answer + distinct web supplement + saved conversation", ["asr", "notes"])
        ];
    }
    public async Task<object> Read(string groupId, string? sessionId) {
        var group = (await store.Groups()).SingleOrDefault(x => x.Id == groupId) ?? throw new InvalidOperationException("Group not found");
        var sessions = (await store.Sessions()).Where(x => x.GroupId == groupId).ToList();
        if (sessionId is not null && !sessions.Any(x => x.Id == sessionId)) throw new InvalidOperationException("Session is not in this group");
        var selected = sessions.FirstOrDefault(x => x.Id == sessionId) ?? sessions.FirstOrDefault();
        return new { group, sessions = sessions.Select(x => new { x.Id, x.Title }), selectedSessionId = selected?.Id,
            agents = Agents(selected), executions = selected is null ? [] : await activity.Read(selected.Id),
            gate = selected is null ? null : await store.NoteGate(selected.Id),
            automaticOrganization = PlaybackEnvironment.AutomaticOrganization };
    }
}
