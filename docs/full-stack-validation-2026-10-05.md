# Playback validation and note repair — 5 October 2026

The full-stack run completed with real local MongoDB, API/browser flows, device controls and approved live providers. Follow-up investigation fixed malformed note citations and repair starvation. The saved 20-minute Week 3 transcription now has **34 of 37 sources referenced**, with three filler-only utterances explicitly deferred. Existing note sections survived every repair unchanged. This accepts the tested recovery behavior and the reviewed filler deferrals; it does not establish independent ASR accuracy or complete semantic coverage.

## Findings and fixes

- **Malformed citations were a defect.** A synthetic live note contained grouped placeholders such as `[cite_T003, T004]`. The previous validator checked only whole brackets matching a narrow pattern. Validation now rejects unresolved aliases and unknown `cite_*` tokens, including grouped tokens, before saving or completing sources. Known aliases still decode; code literals and Markdown links are preserved. Existing saved evidence is retained rather than silently rewritten.
- **One Generate action is a bounded patch.** In the initial lecture result, all 37 transcripts were admitted, but the model wrote points for 19, deferred two and omitted 16. Those omissions honestly remained pending. A single successful call therefore did not demonstrate complete notes. Regression checks verify that follow-up batches retain earlier points and finish remaining input.
- **Deferred gaps could block later speech.** Repair always chose the earliest gap, repeatedly revisiting three filler utterances while substantive later gaps remained. Repair now prioritizes the earliest gap containing speech that is not deferred. Entirely deferred gaps remain in the audit and can be revisited after other gaps. The UI explains this priority and uses “Repair next gap with AI.”
- **The model could invent deferred source IDs.** A later repair was correctly rejected for an unknown deferred source. The Gemini response schema now constrains point sources to supplied aliases/saved citations and deferrals to the actual input batch. With no new input, deferrals must be empty. Application validation remains in place. Google documents string enums and array bounds as supported [structured-output constraints](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/capabilities/control-generated-output).
- Instructions are now `section-notes-v8` / `section-organize-v8`: supporting IDs belong in `sourceIds`; the application renders citations. New inline citation or term-reference placeholders must not be invented.
- The sample runner now recognizes `vad-silence` through the shared chunk completion predicate. API integration assertions now check honest failed Activity records and unchanged pending sources. Memory-test fixture paths resolve relative to the test module.

## Validation

| Evidence | Result |
| --- | --- |
| .NET builds and full offline suite | Passed, including wire-level source-ID schema constraints, malformed citation rejection without note writes, valid retry, bounded follow-up, retention, deferred-gap priority and sync checks. Builds reported zero warnings/errors. |
| Production web build | Passed. Existing large-chunk and dependency annotation warnings remain. |
| Real Docker MongoDB | Conversation persistence/isolation, 125 note-version pagination/restore checks and 900-source sync checks passed. |
| Real API/MongoDB integration | Passed after correcting obsolete note retry-counter expectations; passed again with the note fixes. |
| Browser/API/MongoDB and device controls | Session/group/material/note/audio flows and short microphone/system/both start/pause/resume/stop checks passed. This is not a three-hour hardware recording test. |
| Offline browser behavior | Transcript, library, citations/chat, loading and Reading Sources passed. After the fixes, coverage repair, citation/chat and Reading Sources passed again, including dirty-editor protection and visible stale-base failure. |
| Production loading/math/memory | Cold load about 1.04 s; diagram failure recovery passed; 80 math cycles passed; approximately 126 s / 76 navigation cycles with a 2,700-source fixture passed. This is fixture UI evidence, not a three-hour realtime soak. |
| Approved real Week 3 ASR | First 20 minutes uploaded once as 40 local chunks: 37 OpenRouter Qwen3 transcripts, three local VAD skips, zero manual-retry chunks. Real cited Q&A and note persistence passed through API/MongoDB. No independent recognition/VAD accuracy score was produced. |
| Fresh synthetic live providers | Gemini notes/grounding/translation/Q&A and Jev ranking/cache checks passed. Application note and translation calls also demonstrated honest contract rejection and retry recovery; first-attempt reliability is not claimed. |
| Saved lecture text repairs | Real Gemini 3.1 Flash Lite through production NoteAgent with an isolated memory store, no additional audio upload or MongoDB mutation. References increased from 19 to 34, all earlier sections retained, zero completed-without-reference sources, no large remaining gaps. Final schema-constrained repair passed. |

The remaining three sources contain only a filler utterance and have explicit deferral reasons identifying no substantive course information. Leaving them unresolved is preferable to attaching fabricated factual claims. They still count as unreferenced; they were neither suppressed nor marked completed.

The live investigation also recorded one HTTP 429 and one unknown-deferred-source rejection. The 429 attempt stopped without changing saved notes; bounded resumption subsequently succeeded. Google lists capacity/quota exhaustion among [429 causes](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/api-errors), but this run does not diagnose the account's precise cause. No provider retry redesign, model switch or default-model change was introduced.

## Data preservation and evidence

Compose provides MongoDB; API/UI and WASAPI recording run as Windows processes. The existing `app-temp-mongo-1` container and named volume were reused. Test writes targeted `playback_e2e`; the sample runner cleaned only its own session/audio. The existing `playback_prototype` counts were unchanged after the full run and final fix verification: 7 sessions, 2,078 chunks, 2,016 transcripts, 292 notes, 197 translation versions, 810 term insights, 3,025 activity records, 42 citations, 12 conversation turns, three groups, two note gates and one conversation. No database was cleared.

Detailed local evidence is retained under `app-temp/data/validation/runs/2026-10-05-full/` and is excluded from Git. It includes the original failures, definitive recovered sample result, saved ASR/note inputs, offline replay, before/after repair checkpoints, usage and browser results. `coverage-schema-final-1/live-results.json` records the final 34-reference / three-deferred result. Earlier snapshots and failed attempts remain available. Audio, recognized text, provider payloads, credentials and database files are excluded from the commit.

Independent semantic completeness, human ASR scoring, correctness of the three VAD exclusions, native streaming-provider behavior and three-hour realtime/hardware endurance remain outside this acceptance. Historical saved malformed paragraphs were not automatically migrated.
