# Phase 0 contracts and baseline

Date: 2026-10-07. This records the behavior preserved by Phase 1 and defines the later policy, tree and audio schemas. The proposed schemas below are contracts for later phases; Phase 1 does not write them to MongoDB or expose settings controls. [The plan](ddd-notes-audio-refactor-plan.md) remains the scope baseline.

## Existing contracts preserved

| Contract | Current behavior / constraint | Offline evidence |
| --- | --- | --- |
| Notes HTTP | Existing `/api/sessions/{id}/notes` routes, request field names and response fields remain. POST save uses `{markdown, basedOnVersion?}`; conflicts return 409 with `{error}`. | `NoteApiChecks`: real endpoint delegates and exception middleware, with memory storage; no HTTP listener. |
| History | Append a new version for edit, generation, organization, restore or selected recovery. Historical Markdown, IDs, citations and source links remain readable. Legacy list is oldest-first, capped at the latest 100; history pages are newest-first, limit 1–50, exclusive `before`. | Existing document/recovery checks, API save/version/history/edits/restore checks, legacy BSON readback. Mongo paging remains a query adapter; database readback has not been run in this phase. |
| Saved documents | Collections remain `notes` and `note_gates`. BSON uses `_id`, PascalCase members and nested `_id` for sections/points/citations. HTTP uses camelCase. Missing legacy lists and gate fields keep their existing defaults. No discriminator or schema rewrite is introduced. | `RefactorBaselineChecks.Serialization`, including a note without sections/coverage and a persisted wait. |
| Note input identity | Prompt versions `section-notes-v8`, `section-organize-v8`, `note-gate-v1`; ordered JSON fields, source aliases, hashes and size limits remain. Trigger is excluded from saved gate identity; Stop has a separate flush version. | Scheduling/restart checks, schema/alias checks, input limits and full Week 3 source lookup. |
| Retention | Unselected sections stay; protected edits reject automatic replacement; formulas, conditions and examples must survive. Covered status requires actual written points/citations, not completed source metadata. Omitted sources stay pending; explicit fragments may be deferred. | Document, generation, coverage and foundation checks. |
| Recovery | User deletion produces section tombstones and source suppression. Recovery previews cannot revive intentional deletions; restore is an explicit new protected version. | Document/recovery and API checks. |
| Audio/evidence | Original chunk identity, original ASR, time ranges, retry targets and authorized playback remain. No recordings are moved, combined or removed in Phases 0–1. | Existing recording/ASR checks; quiet timeline characterization. No new hardware or Mongo evidence. |
| Ownership | Existing workspace middleware continues to authorize the same session paths. Worker hosting continues to pass explicit session IDs; auth, ownership and sharing are not redesigned here. | Middleware and route paths remain; existing offline isolation checks. Connected workspace authorization has not been re-tested. |

## Reproducible problem fixtures

These are characterization checks, deliberately expecting current behavior through Phase 1. Change their expectations when the corresponding feature phase ships; do not count them as acceptance of the problematic behavior.

| Reported problem | Reproduction | Next acceptance condition |
| --- | --- | --- |
| Hardcoded Jev admission | `DiagnosticDecision` uses an in-memory HTTP handler returning update probability 0.55 and confidence 0.26. The decision is rejected under 0.70/0.50. No provider is contacted. | Phase 2: separate persisted thresholds, effective settings and policy-revision invalidation. |
| Unchanged wait can last forever | Phase 0/1 reproduced one gate call, zero writer calls and pending speech after 600 seconds/restart. Phase 2 replaces that expectation: one gate call, one writer call, real `wait` retained and local `deadline` recorded. | Phase 2 acceptance now also checks a configured 10-second deadline immediately after restart. |
| Quiet audio placed at the first hourly row | `web/test/refactor-baseline.check.mjs` runs the actual TypeScript timeline function on quiet–speech–quiet–ASR-empty parts. All quiet/empty parts fold into the first row. Original parts remain. | Phase 3: separate chronological quiet runs, compatible original samples joined into playable files, ASR-empty excluded as silence evidence. |
| Small growing notes do not organize | A synthetic two-paragraph TCP section fails `ShouldOrganize`, despite not being organized. The legacy scan interval remains 30 minutes. | Phase 5: changed-revision/dirty-age/cooldown triggers, including small changed documents. |
| Flat hierarchy and retention catch-all | `FlatNotesAndOrganization` opens the synthetic TCP example as one heading with two whole points. Existing formula/condition checks exercise misleading `retains` mappings; old prose is retained under the existing catch-all. | Phase 4: safe topic/subtopic/block operations; Phase 5: separate reviewed rewrite proposals without catch-all dumping. |
| Rules coupled to the host | Original notes lived in one ASP.NET/Mongo/provider assembly. | Phase 1: executable assembly dependency checks; real note routes use Application; mutation/admission rules have one Domain implementation. |

The synthetic TCP Markdown is saved in `RefactorBaselineChecks.FlatNotesAndOrganization`. It reconstructs the *shape* described by the investigation, not the private saved lecture prose or an accepted factual summary. No database example was exported. The repository Week 3 fixture remains the source for longer checks.

## Diagnostic timing contract

The plan's prior TCP investigation reported 13 rejected decisions, latest probability/confidence 0.55/0.26, one Gemini revision around 2.7 seconds and nine pending transcripts. These are inherited observations, not measurements collected by this implementation run. The offline decision fixture reproduces the threshold result only.

| Stage | Clock / source | Existing evidence and gap |
| --- | --- | --- |
| Capture and finalization | Chunk start/end and capture finalization time | Start/end exist; no new finalization event is added before Phase 3. |
| ASR queue and provider | Job queued/started/end times | Existing ASR activity; no new live timing captured. |
| Confirmed input → admission | `ConfirmedAt` to activity start | `ScheduleDelayMs`; absent historical `ConfirmedAt` remains null, not a guessed delay. |
| Gate | Jev adapter elapsed time | `ProviderLatencyMs`, decision values/model/usage. Offline handler timings are not provider benchmarks. |
| Writer queue and execution | Activity queue/start, generation adapter elapsed | `QueueDelayMs`, `ProviderLatencyMs`, usage; draft callbacks remain visible. |
| Persistence and UI delivery | Save completion and sync receive | Not separately instrumented yet. Do not infer them from generation latency. |

Maximum pending wait is an **admission deadline** for Phase 2, not a complete-notes latency guarantee.

## Implemented processing policy schema v1 (Phase 2)

Numbers must be finite. Unknown settings and invalid values return 400. Missing/null override fields inherit; an explicit false or zero remains explicit. Defaults/ranges implement the plan's starting values, not measured optima.

The new `processing_settings` collection stores `{_id, schemaVersion: 1, revision, overrides}`. Workspace keys are `workspace:user:<owner>` (or `workspace:local` without login); session keys are `session:<id>`. Owner keys come from request identity or the stored session owner, never request JSON. Reads do not create documents. Existing notes, transcripts, audio and session documents require no migration.

GET `/api/settings/processing` and `/api/sessions/{id}/processing-settings` returns `{schemaVersion, revision, inheritedRevision, effectiveRevision, overrides, effective, inherited}`. Effective policy identity is `<workspace revision>:<session revision>`. `effective` and `inherited` contain the seven flat fields below. PUT carries `{expectedRevision, expectedInheritedRevision, overrides}`; `{}` resets inheritance. Session PUT checks the inherited revision seen by that tab as well as atomically comparing its own resource revision. Concurrent first writes use the unique `_id`; subsequent writes filter by `_id` and revision. Conflicts return 409. No-op saves preserve the revision. Workspace/session changes never overwrite each other's settings records; resolving a session always uses current workspace values.

Server defaults are typed, validated on startup, and optionally read from the API's `Processing` configuration section (standard configuration binding). Editable policies live in MongoDB. Processing sync records deliver effective settings and admission status incrementally without rereading the lecture.

| Field | Type / range | Default |
| --- | --- | --- |
| jevEnabled | boolean | true |
| probabilityThreshold | number, 0–1 | 0.70 |
| confidenceThreshold | number, 0–1 | 0.50 |
| checkIntervalSeconds | number, 2–30 | 10 |
| maxPendingWaitSeconds | number, 10–120 | 30 |
| silenceBoundarySeconds | number, 0.7–10 | 2 |
| boundarySettleSeconds | number, 0–5 | 2 |

The first five controls are live in Notes settings. Silence/settle fields are persisted and validated for Phase 3; real speech-boundary scheduling is not wired yet, so those controls are not exposed. Organization and audio-combination fields below remain future proposals and are not accepted by the Phase 2 API.

| Future object.field | Type / range | Proposed default |
| --- | --- | --- |
| organization.enabled | boolean | true for new policies; retain explicit disabled preferences |
| organization.changedRevisionThreshold | integer, 1–10 | 3 |
| organization.maximumDirtyAgeSeconds | number, 30–600 | 120 |
| organization.minimumCooldownSeconds | number, 15–300, <= maximumDirtyAgeSeconds | 60 |
| organization.scope | `affected-topics` or `document-outline` | `affected-topics` |
| organization.presentation | `numbered-hierarchy` or `headings` | `numbered-hierarchy` |
| audio.combineQuiet | boolean | true |
| audio.maximumCombinedSeconds | number, 10–300 | 60 |

Additive `note_gates` fields now persist `policyRevision`, `policySnapshot`, nullable `firstPendingAt`/`lastCheckedAt`, `admissionReason`, `evidenceHash`, `settledEvidenceHash`, `settledNoteVersion`, `settledLanguage` and `gateFailure`. Existing retry and versioned flush fields remain. A persisted wait never resets the oldest eligible input age. Settled filler is free until evidence, note/language base, policy or an explicit Stop changes. Old gate records initialize missing fields safely; their age starts from confirmed source time if known, otherwise the first new eligible check. Finalized source/boundary watermarks remain Phase 3.

Current automatic admission reasons: `jev-allow`, `deadline`, `stop`, `gate-disabled`; a local reason following a gate failure adds `-after-gate-failure`. The real Jev decision stays unchanged. Provider failures remain in Activity; writer retries reuse admission with bounded backoff. Stop remains requested across restart, in-flight decision saves and late ASR, and version-conditional acknowledgement cannot clear a newer Stop. Deferred speech stays unresolved in coverage even after its Stop attempt settles. Countdown means admission age, with a two-second scheduler poll and provider execution additional. Manual revision, gap repair and organization retain their explicit paths. Real `speech-boundary` and organization age/revision triggers remain later phases.

## Note tree schema v1 (Phase 4)

Document envelope: `schemaVersion: 1`, `sessionId: string`, `revision: integer >= 0`, `basedOnRevision: integer?`, `nodes: NoteNode[]`, `citations: NoteCitation[]`, `coverage: BlockCoverage[]`, `deletedNodeIds: string[]`, `suppressedSourceIds: string[]`. IDs are stable opaque strings; historical source/citation IDs remain unchanged.

| NoteNode field | Type / constraint |
| --- | --- |
| id | Unique nonempty string within the document |
| parentId | Existing topic/subtopic ID or null for a root topic |
| order | Nonnegative sibling integer; renderer orders deterministically |
| kind | `topic`, `subtopic` or `block` |
| title | Nonempty string for a topic/subtopic |
| body | Markdown string for a block; sole canonical prose body |
| role | For blocks: `definition`, `explanation`, `condition`, `formula-derivation`, `example`, `argument`, `comparison`, `diagram` |
| sourceIds | Block's actual evidence IDs; no invented IDs |
| userEdited | boolean; protects bodies and automatic repositioning |
| version | Positive integer for touched-node concurrency checks |

Topic/subtopic nodes carry structure, not duplicate prose. Blocks may contain math, code, tables and captions. Structural depth is bounded to three levels. Reject unknown parents, cycles, duplicate IDs and invalid sibling ordering. A source citation must resolve within the authorized session or remain explicitly unavailable for copied legacy content.

`BlockCoverage`: `{sourceId, contentHash?, status, reason, blockIds}`; status is `covered`, `deferred`, `pending` or `suppressed`. Coverage refers to actual retained blocks, not a second edited summary and not proof of semantic completeness.

Operations carry `baseRevision`, captured node versions and a bounded selected scope. Initial operations are `add-block`, `update-block`, `move-block`, `create-subtopic`, `reorder-siblings`. Structural organization changes positions/parents without rewriting bodies. Rewrite/merge adds explicit retained-block mappings and a reviewable diff. Protected body edits require explicit user action. Operation bounds are selected and tested in Phase 4; no automatic full-document rewrite is introduced by this schema definition.

The legacy adapter opens existing point bodies whole and protected in the future tree, retaining their text, math and citations. Historical Markdown stays unchanged; only an accepted new revision writes v1. Numbering and Markdown are projections, never separately model-authored labels or duplicate editable bodies.

## Combined audio schema v1 (Phase 3)

| CombinedAudioSegment field | Type / constraint |
| --- | --- |
| schemaVersion | 1 |
| id | Deterministic hash-derived ID from stream identity, ordered children and policy version |
| sessionId, sourceId, streamId | Authorized session, separate capture source and uninterrupted recording stream |
| policyRevision | Revision/snapshot identifying the combination operation |
| startMs, endMs | Original timeline range; end > start |
| format | `{encoding, sampleRateHz, channels, bitsPerSample, blockAlign}` matching every child |
| children | Ordered `{chunkId, sequence, originalHash, frameOffset, frameCount, startMs, endMs}` manifest |
| derivedHash | SHA-256 of the completed WAV; absent while unpublished |
| storageKey | Server-generated relative storage key; absent while unpublished |
| state | `building`, `published` or `failed` |

Offsets/counts are integer **PCM frames**, keeping stereo channel alignment explicit. No raw file path is supplied by a client. `published` requires a valid WAV, matching sample/frame counts and durable manifest/file state. Original hashes and original chunk files remain unchanged. Temporary/failed files are never presented as completed playback.

Join only adjacent finalized no-speech evidence from the same stream/source/session and compatible format. Speech, pause/resume, selected-source changes, format changes, gaps and the duration bound close a segment. `asr-empty` does not establish silence. Do not concatenate WAV headers or shorten timing gaps. Mixed-source playback remains distinct from source-specific combination; retries target original chunks, not a new ASR copy.

Events reserved for later flows: `TranscriptConfirmed`, `SpeechBoundaryDetected`, `NoteRevisionSaved`, `ProcessingPolicyChanged`. They describe committed facts; persisted work state recovers missed notifications. Phase 1 retains existing polling and introduces no broker or event-sourced storage.

## Migration boundaries and remaining evidence

No database reset, index rebuild, audio cleanup, original-file replacement or runtime restart is part of Phases 0–1. No package versions change. The note types lose Mongo attributes but retain the driver's existing ID conventions and serialized fields; offline BSON fixtures verify old-document readback.

The Mongo adapter retains the existing single-host shared semaphore and checks current version/language before insertion. This foundation **does not yet provide a storage CAS across API processes**. The plan's atomic versioned head, durable pending history, language/settings revision and crash-recovery work remain explicit repository/concurrency work for the later migration; do not deploy concurrent note-writing hosts on the strength of these offline checks.

Connected Mongo persistence/restart, workspace authorization, browser/API end-to-end, natural-provider quality, hardware recording and Docker execution remain separate evidence categories. They were not run here. Later tests must use isolated data and preserve the original workspace and recordings.
