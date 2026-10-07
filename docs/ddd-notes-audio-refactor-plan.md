# Playback DDD, notes and audio refactor plan

Date: 2026-10-07. Status: Phases 0, 1 and 2 implemented locally with offline checks. See [contracts and fixtures](ddd-notes-audio-contracts.md), [Phase 0/1 results](ddd-notes-audio-phase01-results.md) and [Phase 2 results](ddd-notes-audio-phase02-results.md). Phase 3 has not started; later-phase schemas remain proposals.

This plan covers the .NET backend in `app-temp/`, its React settings and note/audio presentation, and the checks needed to migrate them in phases. The product requirements in [business-requirements.md](business-requirements.md) remain the acceptance baseline. Each phase should produce a working, reviewable vertical slice before the next begins.

## 1. Requested outcomes and current causes

| Requested outcome | Current evidence | Planned change |
| --- | --- | --- |
| Adjustable Jev confidence in backend and UI | `JevNoteGate.Decide` requires action `update`, probability >= 0.70 and confidence >= 0.50. Both thresholds are hardcoded. | Persist separate probability and confidence settings, validate them on the server and expose them in Notes settings. |
| A shorter, objective, configurable fallback | `NoteAgent.Automatic` requires three waits plus 2,000 characters or additional backlog. An unchanged `wait` returns early forever. | Use elapsed pending age and speech-boundary signals, independently of text length and language. |
| Combine quiet audio into a chunk | Capture writes WAV parts; VAD classifies them separately. The UI combines every quiet period in an hour at the first quiet row. | Combine consecutive finalized quiet chunks into a single playable audio segment/file and show it at its actual timeline position. |
| More frequent, configurable reorganization | Automatic organization is opt-in through an environment switch, scans every 30 minutes, and uses large/exact-text repetition thresholds. | Add a persisted organization policy driven by meaningful document changes, age and a cooldown. |
| Coherent hierarchical notes | Notes are sections containing flat Markdown points. Context selection uses word overlap. Retention checks require old point text verbatim; rewritten points can be appended under `Earlier source-backed points`. | Model a topic/subtopic/block hierarchy, route updates to existing topics, and separate safe structural rearrangement from content rewriting. |
| DDD backend | One web assembly currently holds storage documents, business rules, provider calls and constructor-started workers. | Introduce domain-owned behavior, application use cases, infrastructure adapters and a small API host, migrating one flow at a time. |

Observed in the running TCP 1 session during the investigation: Jev admission rejected all 13 decisions; the latest probability/confidence were 0.55/0.26. One Gemini revision completed in about 2.7 seconds. Nine transcript segments remained pending. These are a diagnostic snapshot, not general provider latency or quality benchmarks.

The saved example was one TCP heading followed by two substantial paragraphs. It explains several distinct ideas without a useful subtopic hierarchy. The structural complaint is supported by that example; the factual correctness of every sentence has not been independently accepted.

## 2. Design direction

Use a DDD modular monolith with four .NET projects. The same application remains one deployment; its internal dependencies become explicit. This is a project-specific design choice informed by Microsoft's [application architecture guidance](https://learn.microsoft.com/en-us/dotnet/standard/modern-web-apps-azure-architecture/common-web-application-architectures). Business rules belong in domain objects, with aggregates sized around consistency needs, as described in Microsoft's [domain model guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model).

Proposed layout:

```text
app-temp/
  domain/          Playback.Domain: business state, invariants and policies
  application/     Playback.Application: use cases, orchestration and ports
  infrastructure/  Playback.Infrastructure: MongoDB, providers, audio and workers
  api/             Playback.Api: HTTP contracts, auth, composition and hosting
  checks/          Existing behavioral harness, organized around the new boundaries
  web/             Existing React application
```

Each project uses feature folders. The folders below express candidate domain boundaries, not one additional project per noun:

| Boundary | Owns | Main consistency units |
| --- | --- | --- |
| Workspace | Session catalog, groups, ownership and policy defaults | Session metadata and workspace preferences; no giant aggregate containing all lecture data |
| Recording and evidence | Recording lifecycle, source streams, chunk identity, confirmed transcripts, materials and combined audio | Recording stream state; individual immutable chunk/evidence records; bounded combined segments |
| Learning notes | Note document structure, revisions, citations, protected edits, coverage and note policies | Current note document and bounded update state; historical revisions stored separately |
| Assistance | Conversations, answers, term explanations and translation work | Conversation and individual processing records |

Dependency rules:

- Domain uses plain C# and has no MongoDB, ASP.NET, NAudio, ONNX, Gemini or Agent Framework references.
- Application references Domain. Use cases load state, call domain behavior, invoke provider/storage ports and persist results. Provider output is an untrusted proposal.
- Infrastructure implements those ports and references Application/Domain. MongoDB documents and provider wire formats stay here.
- API references Application and Infrastructure for composition. Endpoints authorize, validate transport input and call a use case; they do not decide business policy.
- Read projections may query storage directly through an application query port. Writes go through domain behavior and purpose-specific repositories.
- Use explicit constructor injection and concrete use cases. Introduce abstractions at real persistence/provider/capture boundaries; keep simple CRUD simple.
- Introduce in-process domain/application events where one saved change triggers another flow. Recover missed notifications from persisted work state. Events describe committed facts, such as `TranscriptConfirmed`, `SpeechBoundaryDetected`, `NoteRevisionSaved` and `ProcessingPolicyChanged`. Microsoft's [domain event guidance](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation) informs the separation of event handling from domain objects.
- Use `BackgroundService`/hosted services for worker lifecycle, cancellation and shutdown, following [.NET 10 hosted-service guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0).

Keep .NET 10, Microsoft Agent Framework, the existing Gemini/ASR adapters and MongoDB. The refactor does not require a broker, event-sourced storage, generic repository framework or a mediator package. Package upgrades are separate decisions when a phase actually needs them.

## 3. Configuration contract and UI

### Scope and persistence

Proposed scope: server defaults -> saved workspace defaults -> optional session overrides. The effective policy is resolved on the server. Session settings offer `Use workspace defaults`, overrides and `Reset`. Workspace defaults apply to sessions that inherit them; explicit session overrides remain intact.

Store policy schema version and revision. Return effective values, override values and the revision through a dedicated settings API and session sync. Suggested resources are `GET/PUT /api/settings/processing` and `GET/PUT /api/sessions/{id}/processing-settings`; existing routes keep working during migration. PUT carries an expected revision so two tabs cannot silently overwrite each other's settings. Authorize all reads/writes by workspace/session ownership.

Deployment defaults use validated typed options. Editable user policies live in MongoDB; an environment variable alone is insufficient for UI configuration. Invalid, non-finite and out-of-range values are rejected by the server and explained inline by the UI. Published defaults are proposals to be tuned with representative lectures, not tested optima.

### Notes timing and admission

| Setting | Proposed default | Proposed allowed values | Meaning |
| --- | --- | --- | --- |
| Jev gate enabled | On | On/off | Off uses local batching/deadlines and Gemini directly; Activity labels the reason. |
| Update probability threshold | 0.70 | 0–1 | Minimum Jev probability to allow an early update. |
| Decision confidence threshold | 0.50 | 0–1 | Minimum Jev decision confidence. Separate from ASR certainty or note correctness. |
| Decision check interval | 10 s | 2–30 s | How often changed input can be considered. |
| Maximum pending wait | 30 s | 10–120 s | Deadline measured from the first eligible confirmed input in an unresolved batch. |
| Silence boundary duration | 2 s | 0.7–10 s | Continuous quiet across the selected recording sources that requests a final batch decision. |
| Boundary settle grace | 2 s | 0–5 s | Allows the last finalized speech chunk to reach ASR before scheduling that boundary's note job. |

Remove the 2,000-character requirement from normal admission. Character count may remain an input-size safety limit, but it does not decide whether enough lecture time has passed. The initial policy needs no minimum word count or language-dependent character fallback.

UI: a compact Notes settings dialog with Timing, Jev decision and Organization groups. Use sliders with numeric inputs where helpful, visible units, reset controls and one-line descriptions. Thresholds are advanced controls. Show a live status such as `Waiting for continuation · 18 s / 30 s`, `Writing notes`, or a real failure; the wait countdown is an admission deadline, not a promise of completed notes.

### Organization

| Setting | Proposed default | Proposed allowed values | Meaning |
| --- | --- | --- | --- |
| Automatic organization | On for new policies | On/off | Existing explicit disabled preferences remain disabled. |
| Reorganize after changed revisions | 3 | 1–10 | Counts actual content/structure changes since the last successful organization. |
| Maximum dirty-note age | 120 s | 30–600 s | Upper admission age for a changed document waiting to be organized. |
| Minimum organization cooldown | 60 s | 15–300 s | Limits repeated organization/provider work. Must not exceed maximum dirty-note age. |
| Scope | Affected topics | Affected topics/document outline | A document-wide operation can reorganize the outline without sending the full lecture prose. |
| Note presentation | Numbered hierarchy | Numbered hierarchy/headings | Numbered mode renders topics as `1`, subtopics as `1.1`, and detail lists as `a`, `b`. |

Organization runs when the change threshold OR dirty-age deadline is reached and the cooldown permits it. No change means no repeated organizer call. Pending note generation takes priority; organization must not occupy all generation capacity. Show `last organized`, dirty revision count, due reason and cooldown. Keep an explicit Organize action for immediate use.

### Combined audio

| Setting | Proposed default | Proposed allowed values | Meaning |
| --- | --- | --- | --- |
| Combine consecutive quiet audio | On | On/off | Produces one playable quiet segment from adjacent finalized no-speech chunks. |
| Maximum combined duration | 60 s | 10–300 s | Bounds files, manifests, latency and timeline rows during long silence. |

Combine only within one session, source, uninterrupted recording stream and compatible audio format. Speech, pause/resume, source changes, format changes and real timeline gaps close a segment. `asr-empty` alone is not proof of silence.

Settings take effect on the next eligible scheduling/segment operation, without an application restart. Threshold/timing changes invalidate an obsolete saved `wait` through the policy revision. Each in-flight job records its policy snapshot. Output-language/style edits and document edits retain stale-result protection. Turning automatic work off stops new admission; a provider request already running is reported honestly and may finish. Audio policy changes affect new combinations; already published files keep their identities.

## 4. Objective scheduling behavior

Persist a small note-work state: input identity, policy revision, first-pending timestamp, source/boundary watermark, last attempted trigger, saved Jev decision and bounded retry state.

Admission order:

1. Recover written source status from the saved document and inspect eligible confirmed input.
2. If there is no new actionable input, do not call Jev or Gemini.
3. Evaluate Stop, finalized speech boundary and maximum-age triggers BEFORE returning for an unchanged Jev `wait`.
4. A Jev allow can write earlier than the deadline. A wait retains input but cannot defeat a local deadline.
5. At a deadline/boundary, submit the pending evidence to the writer with the real reason and appropriate uncertainty. This is a local policy decision, never a fabricated Jev allow.
6. The writer may explicitly defer filler or an incomplete fragment; it may not silently mark unwritten material completed.
7. Persist the attempted batch identity and boundary watermark. A no-change/deferred result does not immediately regenerate on the next tick. New input, a new explicit user action or a materially changed policy can make it eligible again.

A silence boundary must observe the selected sources and committed ASR watermarks. If a last speech chunk is still processing after the grace period, write currently confirmed input and let its later confirmation create the next batch. Stop records a flush request until finalized ASR and the resulting note dispositions are settled. Late ASR must not be dropped or create duplicate coverage.

Jev failure is recorded as failure. The local deadline can still admit Gemini if it is available; Gemini failure retains sources and uses bounded retry/backoff. Provider failures and clearly labelled degraded admission remain distinct from successful decisions.

Measure capture/finalization delay, ASR queue/provider time, note scheduling wait, Jev time, Gemini queue/provider time, persistence and UI delivery separately. For a 30-second maximum wait, admission occurs at the first eligible scheduler tick after the deadline; provider execution time is additional. No claim of an end-to-end 30-second completion SLA.

## 5. Audio combination and provenance

Interpret the clarified requirement as one combined playable chunk, including a materialized WAV file, rather than only an expanded/collapsed list.

- Original chunks remain immutable recovery/evidence objects with their session/source/sequence/hash identities.
- A `CombinedAudioSegment` owns an ordered child-chunk manifest, absolute time range, format, derived-file hash and sample offsets back to each child. It is a playback projection, not another ASR input.
- Stream compatible WAV payloads through a proper WAV writer; never concatenate WAV headers. Preserve sample data. A timing gap closes the segment rather than shortening the recording timeline.
- Create the derived file with bounded buffers and a temporary file, validate it, then publish its manifest/file state atomically enough for crash recovery. Idempotent identity derives from stream identity, ordered child identities and combination policy version.
- Publish closed segments on speech, duration limit, pause or Stop. The current quiet segment can be displayed as one growing row while its playable data comes from the completed child chunks. It need not rewrite a large derived file every eight seconds.
- Keep the row at the quiet period's chronological location. Never combine separate quiet periods across intervening speech into the first quiet row of the hour.
- Original transcript/citation IDs continue to resolve to the correct audio and time. A combined row can reveal its child chunks; ASR retry still targets the original chunk.
- Source-specific playback and mixed playback remain distinct. Include derived files in the existing authorized session cleanup flow when a user explicitly deletes that session.

Combining files changes chunk organization. Concatenation alone does not reduce PCM byte size; retaining originals adds derived-file storage. Automatic deletion or replacement of originals is outside this plan.

## 6. Notes with a useful hierarchy

### Canonical document

Introduce a versioned note tree with stable IDs, parent relationships and sibling order. Topic/subtopic nodes organize content blocks. Blocks contain their actual content and citations, with a limited role such as definition, explanation, condition, formula/derivation, example, argument, comparison or diagram. Start with a bounded depth of three structural levels. Source coverage refers to stable blocks; it remains separate from a claim that the prose is semantically complete.

The structured document is canonical. Markdown, numbering, table of contents and editor views are projections of it. The renderer produces numbering consistently; the model does not invent `1`, `1.1` or alphabetical labels independently on every update. A block can contain math, code or a table without losing its source association.

For the saved TCP example, a layout target is:

```text
1. Transmission Control Protocol (TCP)
   1.1 Purpose
       a. Reliable delivery between devices.
   1.2 Connection setup
       a. Connection-oriented communication.
       b. Three-way handshake, to the extent explained in the source.
   1.3 Reliability and flow control
       a. The source-backed discussion of loss and retransmission.
       b. Preventing the sender from overwhelming the receiver.
```

This illustrates organization of already saved subject matter. Actual rewritten claims must be checked against the transcripts. Examples, formulas and comparison tables appear when the evidence supports them; the writer must not invent material to fill a standard template.

### Update behavior

- Supply a compact outline and relevant evidence so the writer can choose an existing topic/subtopic before creating another. Replace keyword-only section selection with an explicit topic-routing proposal in the same bounded generation call.
- Return bounded operations such as add a block, update a block, move a block, create a subtopic, or reorder siblings. Validate known IDs, allowed parents, depth, cycles, touched-node limits, source mappings and base revision.
- New material is incorporated under the relevant topic. A correction preserves the relationship to the earlier statement and marks what is superseded. It is not pasted as an unrelated new section.
- Organization first moves, groups and nests existing blocks without rewriting their bodies. This permits meaningful structure while retaining exact content and citations.
- Content rewriting/merging is a separate operation with explicit retained-block mappings and checks for formulas, code, numbers, conditions, uncertainty and examples. Stable IDs and citations alone cannot prove preservation of meaning.
- If a rewrite cannot pass retention checks, preserve the prior block in its proper position and surface a reviewable proposal. Do not append the entire old prose under an `Earlier source-backed points` catch-all.
- Initial automatic organization uses the safe structural mode. Riskier rewrite/merge proposals have a preview/diff; semantic acceptance is required before enabling broader automatic rewriting. The plan does not weaken the existing anti-loss guarantee to improve appearance.
- User-edited bodies remain protected. Reordering protected content requires an explicit selected-scope action or user opt-in. Unsaved frontend drafts keep the existing conflict/recovery flow.
- Preserve one canonical content body per block. Coverage and operation metadata must not become another independently edited prose copy.

Legacy notes open through a read adapter mapping existing sections/points into the tree. Saved historical Markdown remains unchanged. The first accepted new version writes the new schema. Legacy points initially remain whole protected bodies; automatic migration must not guess how to split meaning.

### Quality acceptance

Evaluate the repo Week 3 material and the observed TCP example for topic placement, useful hierarchy, definitions, worked examples/derivations where present, preserved qualifications, low duplication, valid citations and treatment of uncertainty. A successful build or a nonempty note is insufficient. Use deterministic fixtures to verify mechanisms and a separately authorized small natural-provider run plus semantic review to judge generated quality.

## 7. Implementation phases

| Phase | Deliverable | Exit condition |
| --- | --- | --- |
| 0. Baseline and contracts | Capture current contracts, saved examples, diagnostic timing and a requirement-to-check matrix. Define policy/tree/audio schemas and the vocabulary above. | Each reported problem has a reproducible fixture; preserved behavior and migration boundaries are recorded. |
| 1. DDD foundation for notes | Create the four-project dependency structure. Extract note admission policy and note mutation rules into Domain; introduce note/settings/evidence ports and application use cases; retain existing Mongo/API adapters. | Domain builds independently; notes API and offline note/coverage/history checks retain behavior. No duplicate rule implementation. |
| 2. Adjustable timing and Jev | Implement persisted backend settings and Notes timing UI, policy-revision invalidation, local maximum wait, durable Stop handling and visible admission reasons. | Jev can wait forever in a fixture and notes are still admitted at the configured deadline. Threshold changes and restart do not leave a stale wait. Filler produces no repeated paid loop. |
| 3. Audio domain and combination | Extract recording/evidence rules and audio adapters, add speech-boundary events/watermarks, implement combined quiet files/manifests and timeline/player support. Wire real silence boundaries into Phase 2 scheduling. | Consecutive quiet parts play as one segment; speech/pause/source/format boundaries split it; playback and old citations preserve time and identity. Crash/retry produces no duplicated segment. |
| 4. Hierarchical notes | Add the canonical tree, schema adapters, bounded update operations, topic routing, deterministic numbering and safe structural reorganization. Adapt preview, editor, citations, history and recovery. | Saved samples show useful topics/subtopics; all preexisting bodies and sources survive structural changes; edits and historical restore round-trip. |
| 5. Configurable organization and rewrite proposals | Add organization settings in backend/UI, changed-revision and dirty-age triggers, cooldown and affected-topic/outline scope. Add guarded rewrite/merge previews. | Small growing notes reorganize at the configured trigger; idle notes do not call models; generation remains responsive; failed rewrites do not dump old prose at the end or lose evidence. |
| 6. Finish the .NET migration | Migrate remaining transcription/translation, terms, Ask, workspace/auth and read projections in individual slices. Replace constructor-started loops with hosted services and finish repository/concurrency boundaries. | All endpoint families use application use cases; domain types have no framework/provider/storage coupling; legacy business-rule paths are retired after equivalent checks pass. |
| 7. Integrated acceptance and rollout | Exercise settings, recording, note updates, organization, history, citations, Ask and workspace isolation together. Complete isolated Mongo/browser checks, an authorized live sample, restart recovery and long-session load evidence. | Each requirement passes its observable checks; natural note quality is reviewed; deployment compatibility and known limits are documented. |

Phase 1 is deliberately limited to notes. Phase 2 delivers the urgent delay/configuration fix before the full backend migration. Phase 3 supplies the real audio boundary used by the pause-triggered note behavior. Organization controls ship with working organization behavior in Phase 5, not as ineffective controls in an earlier UI.

Suggested review units inside Phase 6: recording/ASR recovery; translation; terms/explanations; conversations/Ask; workspace/security; sync/query projections; host/composition cleanup. Finish and verify one unit before moving the next. The translation file already modified in the workspace must be reviewed as existing WIP and preserved throughout.

## 8. Migration and reliability rules

- Evolve schemas additively with versioned readers. Do not clear `playback_prototype`, overwrite old note versions or renumber source IDs.
- Use old-to-new adapters during each phase. Switch a complete use case to the new path once its checks pass; keep one active source of business rules.
- Retain ownership in settings, jobs, evidence queries and playback manifests. Background work carries explicit workspace/session context instead of relying on an HTTP request identity.
- Repository writes enforce expected revisions in storage. Moving the current in-process semaphore is not sufficient protection against competing writers or stale provider results.
- Keep the standalone MongoDB setup viable. Before choosing a multi-document transaction, verify deployment support. A practical design is an atomic versioned head with durable pending history/work markers; history is flushed idempotently before another head advance. Verify crashes between these steps instead of assuming cross-collection atomicity.
- Provider work occurs outside write locks. Persist operation/input identities and retry disposition; a crash after a saved note recovers source status from the written blocks. Completed provider work is not silently labelled successful if saving failed.
- Recovery scans remain bounded and index-backed. In-process events improve responsiveness; persisted state supplies recovery after lost events/restart.
- Combined audio publication is restart-safe. Partial files are never offered as completed playback; original audio remains usable if a combination fails.
- Planning and offline implementation do not restart an active recording. Apply a host restart at a safe stopped-recording boundary when needed.
- Documentation and validation evidence identify offline fixture, cached provider, local Mongo, browser, live-provider and hardware results separately. A Docker build does not prove Windows capture or Cloud Run capture support.

## 9. Behavioral checks and acceptance matrix

| Area | Required evidence |
| --- | --- |
| Policy | API range/cross-field validation; persistence/readback; workspace inheritance; session overrides; two-tab revision conflict; authorization; policy change invalidates wait; in-flight snapshot is visible. |
| Note timing | Fixed-clock boundary tests before/at/after maximum wait; unchanged wait; restart; ASR late arrival; Stop; dual-source silence; provider error/backoff; no duplicate deferred batch generation. |
| Audio | Synthetic quiet-speech-quiet recordings; same-source joining; format/gap/pause splitting; sample equality and child offsets; duration; playable WAV headers; bounded buffers; retry/crash publication; original hash unchanged. |
| Structure | Useful hierarchy and deterministic numbering; topic routing; no cyclic/invalid tree; source links; math/code/example preservation; safe rearrangement; rejected misleading retains; user-edit protection; legacy/history/restore round-trip. |
| Organization | Revision/age/cooldown boundaries; actual enabled/disabled behavior; no idle call; one active job per document; regular notes take priority; explicit organize; provider failure keeps prior version. |
| API and UI | Settings save/reload/reset, live wait reasons, combined playback and child expansion, chronological quiet rows, narrow-screen dialogs, draft conflicts, source navigation and history. |
| DDD | Project dependency checks; no infrastructure attributes in Domain; real repository and provider seams; migrated routes have one business-rule path; hosted worker startup/shutdown and restart recovery. |
| End to end | Isolated `playback_e2e` DB, original workspace counts/data preserved, source-backed notes through save/restart/readback, workspace isolation, bounded three-hour fixture load. Natural lecture quality and hardware evidence remain separate. |

Reuse the existing checks harness and browser fixtures. Add tests around the identified failure modes and domain invariants rather than tests mirroring trivial DTOs. Use `TimeProvider` and in-memory adapters for scheduling; use the repository copy of Week 3 for reproducible evidence.

The .NET project references and verification commands must be updated as projects move. Current starting commands are:

```powershell
dotnet build app-temp/api/Playback.Api.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/ -- --notes
npm.cmd --prefix app-temp/web run build
git diff --check
```

New projects may need a separately authorized package restore. Live-provider requests/audio upload, software installation, container startup and destructive DB changes follow `AGENTS.md`; those operations are not performed as part of writing this plan. No database deletion or destructive migration is planned.

## 10. Decisions to revisit after each phase

The requested features and phased DDD migration are confirmed scope. Policy defaults/ranges, exact project/folder names, tree role vocabulary and API route names above are proposals. Use phase evidence to tune them. The interpretation of audio combination is a real derived playable chunk with retained original provenance.

Keep the next review focused on the completed phase: changed behavior, affected contracts, migration/readback results, validation and the next remaining risk. Do not report natural note quality as accepted until the hierarchy and source-supported meaning have actually been reviewed.
