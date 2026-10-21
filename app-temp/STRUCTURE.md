# Prototype code structure

## Web

`web/src/main.tsx` mounts the only page, `pages/PlaybackPage.tsx`, inside the
TanStack Query provider. Read the page first: it lays out the header with the
recorder, the library sidebar, the notes and transcript columns, the audio
player bar and the Ask panel, and hands each the session being shown.

| Folder | Holds | Start with |
| --- | --- | --- |
| `pages/` | The page layout only | `PlaybackPage.tsx` |
| `features/library/` | Sessions and groups in the sidebar, which session is shown, reloading what the page shows | `LibrarySidebar.tsx`, `refreshWorkspace.ts` |
| `features/recording/` | Record/pause/stop, the recorder's status, the input level meter | `Recorder.tsx` |
| `features/transcript/` | The transcript timeline: its entries, virtual list, row kinds (`rows/`), ASR status and session settings, the session rerun dialog and its progress row | `TranscriptPanel.tsx`, `RerunDialog.tsx` |
| `features/player/` | Playback of saved audio through one `<audio>` element | `useAudioPlayer.ts` |
| `features/materials/` | Teaching materials: list and attach | `MaterialsList.tsx` |
| `features/notes/` | Reading, editing and saving notes; locking and instructing blocks and topics; history and restore | `NotesPanel.tsx` |
| `features/terms/` | Key-term highlights, saved explanations, Jev decisions | `TermHighlight.tsx` |
| `features/ask/` | Questions about the session, answers and saved conversations | `AskPanel.tsx` |
| `features/activity/` | The AI execution log and a group's AI flow | `ActivityPopover.tsx` |
| `features/sources/` | Citations: the AI's citation syntax, source chips and links, jumping to a source, following a superseded source to its rerun segment (`lineage.ts`) | `CitedMarkdown.tsx`, `useRevealSource.ts` |
| `components/` | Generic UI that knows no feature: layout shells, shared controls (`Button`, `IconButton`, `Dialog`, `Menu`, …), `Icon`, the Markdown renderer (`markdown/`) | — |
| `lib/` | The API client and response schemas (`backend/`), the query client, browser UI state, health, time formatting | `backend/client.ts` |
| `styles/` | `app.css`: Tailwind, theme tokens and page globals; `markdown.css`: rendered Markdown | `app.css` |

### State

- Data read from the API lives in the TanStack Query cache (`lib/queryClient.ts`),
  keyed by session where it belongs to one: health (`lib/useHealth.ts`), recorder
  status (`recording/captureQuery.ts`, polled every 250 ms by `useCapture`), the
  session list, groups and one session (`library/libraryQueries.ts`; the shown
  session is re-read every 4 s by `useSelectedSession`), AI activity, saved
  conversations, note history, the AI flow and the edit log.
- `lib/store.ts` holds browser-only UI state: which session is shown, view and
  panel toggles, the question being typed and its selected passage, `busy` and
  the page error. `runAction` marks a user action busy and shows its error.
- `library/refreshWorkspace.ts` reloads health, recorder status, the lists and one
  session, then shows that session. Opening a session and every write call it.
- A session is read through the paged sync in `lib/backend/sessionSync.ts` when
  the API offers it, otherwise in one request.
- `notes/useNoteDraft.ts` holds the editor text and the saved version it started
  from. A new saved version replaces the text only when nothing is unsaved;
  otherwise saving waits until the user loads the new version, and the
  replaced text stays recoverable.

### Flows

- Open a session: `library/SessionItem` → `openSession` → `refreshWorkspace(id)`
  → `selectedSessionId` → `useSelectedSession` in the page → the columns.
- Record: `recording/Recorder` → `useCapture` → `/capture/*`. When the recorder
  saves audio for the shown session, `useSelectedSession` reads it again and
  `transcript/Timeline` replaces the live row with the saved one.
- Save notes: `notes/NotesPanel` → `useNoteDraft.save` → `/notes` with the base
  version → `refreshWorkspace`.
- Ask: `ask/AskPanel` → `useAsk` → `askStream` in `lib/backend/client.ts` (or
  `/ask` when the API does not stream) → the saved conversation is read again.
  `ask/askAbout.ts` starts a question from a key term or a transcript selection.
- Open a citation: `sources/useRevealSource` switches to the transcript and
  dispatches `REVEAL_SOURCE_EVENT`; `transcript/VirtualList` mounts, opens and
  highlights the passage.

### Styling

Components draw themselves with Tailwind CSS v4 utilities in their `className`;
no component has its own stylesheet. `styles/app.css` loads Tailwind and defines
the theme tokens (the colours, the `xs` breakpoint just above 390 px phones, the
recorder and note animations) and the few page globals Preflight does not cover:
the page never scrolls, buttons show a pointer, focus shows an accent ring.
`md`, `lg` and `xl` are Tailwind's defaults (768, 1024, 1280 px). Conditional
classes go through `clsx`. A look repeated across files is a component in
`components/` (`Button`, `IconButton`, `Segmented`, `Chip`, `Tag`,
`CitationChip`, `Dialog`, `Menu`, `PopupHeader`, `EmptyState`); one repeated
within a file is a named class string there. There is no `@apply`.

`styles/markdown.css` is the exception. react-markdown, KaTeX and Mermaid create
those elements, not our JSX, so their look stays CSS under the `[data-markdown]`
root. A component that shows Markdown adjusts it from outside with a descendant
variant, such as `**:data-markdown:max-w-[72ch]` on the notes body.

## .NET backend

Four .NET 10 projects form one deployment. Read API `Program.cs` for HTTP/auth setup and `infrastructure/ServiceRegistration.cs` for concrete service/worker composition.

| Project | Owns | Depends on |
| --- | --- | --- |
| `domain/` | Evidence/workspace/assistance state, the note tree and its mutation, citations, the fixed audio policy (`AudioPolicy`) and shared validation | .NET only; no packages |
| `application/` | Notes, transcription, translation context/workflow, terms, Ask, activity, workspace/recording/chunk commands and purpose-specific repository/provider/query ports | Domain only |
| `infrastructure/` | Mongo storage/BSON mappings, Gemini/Agent Framework, OpenRouter text and ASR, Jev, VAD/WAV/WASAPI/native display, bounded sync and hosted workers | Application/Domain; unchanged provider/audio/storage packages |
| `api/` | HTTP request mapping, authentication/CSRF/ownership middleware, static hosting and composition; translation host remains here while its preserved WIP awaits commit approval | Application and Infrastructure |

The migrated public CLR names retain some `Playback.Api.*` namespaces for existing callers. They live in the new assemblies; executable dependency/model ownership checks prevent a hidden project reference back to the host. Compatibility adapters contain delegation, with one active rule/use-case implementation.

| Flow | Path |
| --- | --- |
| Recording | `RecordingCommands` authorizes/control transitions through `IRecordingControl`; WASAPI retains per-source original WAVs and durable stream identity |
| Saved transcription | Hosted `AsrQueue` invokes application `Transcription`, with audio preparation, provider and repository ports; confirmed originals and provider identity remain separate from display/translation |
| Transcript second pass | `TranscriptReview` admits new confirmed chunks after 30 s or four chunks per source/stream; `TranscriptReviewWorker` persists completion/retries and retains first-pass text. Corrections use the note session lock, invalidate translations, and rerun notes once per changed batch. Original audio and source IDs stay stable |
| Session rerun | `SessionRerun` (`application/Recording`) plans windows with `RerunWindows.Plan` (`domain/Audio`): consecutive saved chunks of one capture stream, closed at the first pause (a chunk sealed under 7.5 s, or a quiet chunk) after the requested 20–80 s (or 20 s later without one) and never past 90 s; an earlier window is never split. Each window is concatenated by `IRerunAudio` (`WavCombinedAudioFiles`, `rerun_<sha256>`), prepared by the same VAD step and sent with `Pass: rerun-<job>`. A changed result is saved as one segment (`Kind = window`, `ChunkIds`, `FirstPassOriginal`) that supersedes its rows inside the note lock (`IRerunRepository.Supersede`: segment first, then rows claimed only while active, undone together on a race). The job and its windows persist in `rerun_jobs`; `SessionRerunWorker` resumes at the next pending window and yields to a live ASR backlog. Notes afterwards: incremental (`NoteWorkflow.WritePending`: the five-minute windows whose transcript changed are written again) or rebuild (`NoteWorkflow.Rebuild`). `POST/GET /api/sessions/{id}/rerun`, `POST …/rerun/cancel`; the page reads the job as the `rerun` sync record |
| Lineage | `Transcript.SupersededBy` keeps replaced rows; every evidence read (`Session`, sync, translation, pending notes, second pass) uses active rows only. `Lineage.Apply` (`domain/Evidence`) maps a note's citations, block sources and raw IDs through supersession chains; terms are remapped the same way, and `MongoNoteRepository` applies it to the previous version before saving the next. Stored notes keep old IDs until their next version. `LineageGraph` and `GET /api/sessions/{id}/lineage?node=&depth=` return covers/supersedes/cites/contains/parent/mentions edges. The page receives the map as the `lineage` sync record (`supersessions`) |
| Concept graph | Optional LightRAG sidecar (`graph/server.py`, loopback, one workspace per session, local file storage under `data/graph`). `ConceptDocuments` builds one document per source-minute of active rows; `ConceptIndex` (driven by `ConceptGraphWorker` on `SourceChanged`, with backoff) sends changed hashes and deletes emptied minutes. `ChatAgent` asks it for passages first (`Concept graph query` activity) and ranks them ahead of keyword matches; off, empty or failing falls back to keywords. Never used for impact analysis. `LightRagClient`: fixed `127.0.0.1:PLAYBACK_GRAPH_PORT`, no redirects, 2 MB responses, refused offline, off unless `PLAYBACK_CONCEPT_GRAPH=yes`. `GET /api/sessions/{id}/concepts`, `POST …/concepts/index` |
| Quiet audio | `CombinedAudioWorkflow` + `QuietAudio` plan bounded stream-compatible manifests; infrastructure streams/validates/publishes WAV payloads and original hashes |
| Notes | `NoteWorkflow` (`application/Notes`) writes notes at three speeds. While recording: a live jot for each finished minute (topic `live`) and a segment for each finished five-minute window (`NoteSegment`: blocks, concepts, corrections). After Stop: an outline of the whole lecture, then one polish call per top-level topic. `NoteTimeline` cuts the active transcript into windows with stable aliases (`T1`…, ranges `T3-T7`); a window closes when later speech exists, at Stop, or after a minute without new confirmed speech. `NoteLayout` lays out the topics from the segments (draft topics `segment-N`, finished topics `topic-…`). A window's `InputHash` changes when its transcript changes (a second pass or a session rerun), so that segment is written again, and only the finished topics whose `SegmentBlockIds` came from it are polished again. Edited and locked blocks are never replaced. A failed step waits 10 s, 1 min, 5 min, then 15 min. Prompts, schemas and token caps are the `NoteTask`s in `NotePrompts` (`three-speed-v1`); every input starts with the session's materials, so providers can cache it. `NoteCommands` handles block edits, locks, history and restore |
| Note writers | The writer is an `INoteWriter` chosen once by `NoteWriters` (`Notes/NoteProviderAdapters.cs`): `PLAYBACK_NOTE_MODEL` (`openrouter:<model>[@reasoning]` or `vertex:<model>`), else Qwen3.8 Flash on OpenRouter when its key exists, else the Gemini model. `Complete(task, input)` sends the task's schema (OpenRouter strict `json_schema`; Gemini through `GeminiLanguageModel.ResponseFormat`). The OpenRouter writer streams (SSE), so the activity draft grows as the reply arrives; a stream that errors, drops, stays silent 45 s or passes 100 s fails as transient. An OpenRouter call has three layers (`Notes/NoteWriterFallback.cs`): Qwen3.8 Flash, then the models in `NoteWriters.ModelFallbacks` (GLM-5.3-Flash), then Gemini; at most three OpenRouter attempts within 60 s. `NoteActivityAdapter` shows the writer and model that answered, with a "Served by", "Model fallback:", "Retried:" or "Fallback:" note |
| Note script | For Traditional notes, `Notes/OpenccNoteScript.cs` converts Simplified characters in new bodies, titles and replacements with OpenCC `s2hk` (OpenccNetLib) before they apply. Simplified-only characters change (OpenCC's candidate lists plus a short list of characters modern Traditional never uses), and so does a shared character next to one. A word made only of shared characters (示范, 根据, 背后) changes when OpenCC's phrase segmentation finds it and every character it changes is on `PhraseCharacters`, the shared characters Hong Kong writes only in Traditional form; Hong Kong usage such as 了解, 公布, 台下, 干擾, 公里, 游泳 or 着 stays, and forms Hong Kong writes both ways (上周/上週, 周末/週末) are left to the writer prompt's Hong Kong Traditional instruction. `NoteScriptChecks` holds the Week 3 examples both ways |
| Owner actions | `NoteWorkflow.Instruct`: an instruction for the whole notes, a topic or one block (a comment) rewrites it directly (`POST /notes/instruct`), with related segment blocks found through the segments' concepts. `POST /notes/generate` (Revise with AI) closes every window and finishes; Rebuild notes writes everything again from the transcript. `NoteCommands.LockBlock` locks a block so no AI path changes it (`POST /notes/block/lock`) |
| Note durability | One expected-version `note_heads` CAS plus pending-history marker; history flush is idempotent and recovered before another head advance, without transactions |
| Terms/Ask | Application `TermReview` / `ChatAgent` use storage/provider ports; domain owns term candidates/ranking rules and source aliases |
| Workspace/auth | Application commands and shared domain validation/credentials; API owns cookie/CSRF/same-origin transport and ownership middleware |
| Page sync | Application query port to the existing bounded projection: paged/fragments first read, record deltas, zero idle records; restart invalidates cursors |

Processing workers are hosted services. Their constructors do not launch scans. Provider execution is outside note write locks; durable head/retry/work markers supply recovery. The prepared translation change preserves the original `targets.First().SessionId` behavior.

Language/style checks use a current policy snapshot plus the existing per-process coordination. Atomic note CAS protects competing note writers; running several API hosts still needs real acceptance before a horizontal-scaling claim (note failure backoff is held in memory). Windows capture and native Cantonese conversion remain Windows capabilities; a Linux build does not establish their runtime support.

See the [phase plan](../docs/history/ddd-notes-audio-refactor-plan.md), [Phase 6 results](../docs/history/ddd-notes-audio-phase06-results.md) and [acceptance evidence](../docs/history/ddd-notes-audio-phase07-results.md).

## Checks

`checks/Playback.Checks.csproj` is one console project. Read its `Program.cs`
first: it lists every check group and what the group needs. The folders follow
the same question, because it decides whether a check may run now.

| Folder | Needs | How to run |
| --- | --- | --- |
| `Offline/` | Nothing: in-memory HTTP and storage, no network, no database | `dotnet run` with no flag runs all of them; `--notes` runs the note and sync checks; `--sample-audio`, `--sample-audio-preview` and `--vad-sample` read the Week 3 sample, as does `long-lecture-check.py` |
| `LocalMongo/` | MongoDB on localhost, database `playback_e2e` | `--conversation-check`, `--notes-store-check`, `--session-sync-store-check`; `api-integration.check.mjs` also needs the offline API on 5079 |
| `Live/` | Paid providers and an explicit opt-in | `pnpm.cmd test:gemini-live`, `test:jev-live`, `test:asr-live`, `test:sample-audio-live`; `--asr-compare --live` |
| `Validation/` | Writes evidence under `data/validation/runs/` | the `run-*`, `week3-*` and `validation-*` scripts and their flags; offline unless given `--live`. The `poc-*.mjs` scripts (Node, no build) are the 2026-10-21 proofs of concept for three-speed notes, ASR windows with context and graph-based note updates; they cache every reply under `data/validation/runs/2026-10-21-poc/` ([results](../docs/poc-three-speed-notes-asr-context-2026-10-21.zh-HK.md)) |
| `Fixtures/` | — | `MemoryStore`, the fixture note model (`FixtureNoteModel`: one reply per note task), Jev HTTP fakes, `TestClock`, synthetic lectures, the Week 3 sample reader |

Offline check files are named after the product concept they protect
(`AskChecks`, `ThreeSpeedNoteChecks`, …) and print one line when they pass.
`MigrationChecks` reads legacy BSON documents;
`NoteFoundationChecks` verifies actual assembly dependencies. Memory stores use
the database-free base constructor, so a missed override cannot reach MongoDB.
`Expect` is the only assertion helper. It throws `CheckFailed`, never
`InvalidOperationException`, because the product rejects bad input with that
exception and many checks expect it. `dev.mjs` checks that the NuGet files named
in the ignored `obj/` restore assets still exist before `dotnet run --no-restore`.

```powershell
dotnet build app-temp/api/Playback.Api.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
npm.cmd --prefix app-temp/web run build
```

Web: `web/test/` holds the browser checks, named after what they protect. The
checks below answer every API request from fixture data, so they need no API,
MongoDB or network. Run them from `app-temp/web` against Vite dev on port 5174
(`$env:PLAYBACK_OFFLINE_TEST="yes"; npm.cmd run dev -- --port 5174`), or against
`vite preview` by setting the check's base-URL variable:

- `library-sidebar.check.mjs`: sessions and groups, drag and keyboard moves.
- `transcript-recording.check.mjs` (with `PLAYBACK_OFFLINE_TEST=yes`): timeline,
  ASR states and retry, player, settings, terms, activity, recording controls,
  Ask failures, and an API without the newer capability flags.
- `session-loading.check.mjs`: loading state; a failed read keeps the draft.
- `session-rerun.check.mjs`: a rerun window shown once with its first pass, the Rerun dialog request, progress and Stop, Rebuild notes.
- `notes-citations-chat.check.mjs`: citations, diagrams, topic tree, saved
  terms, chat failures and conversations, layout bounds.
- `notes-reading-sources.check.mjs`: Reading/Sources, 1,350 and 2,700 rows,
  history, AI flow, edit conflicts and draft recovery.
- `cold-loading.check.mjs` and `markdown-math.check.mjs`: lazy diagram and maths
  loading. `markdown-math` blocks the built `MathFormula-*.js` chunk, so it needs
  a production preview (`MATH_BASE_URL`, default port 5180).

Checks find elements the way a reader does: by role and accessible name, by
label, or by an existing `data-*` hook (`data-transcript-id`, `data-markdown`,
`data-virtual-row`, `data-chunk-id`). An element with no name gets a
`data-testid` named after its component (`audio-row`, `note-content`). They never
select by CSS class, because classes are styling and change with it.

`notes-snapshot-performance`, `memory-retention` and
`validation-replay` replay saved runs from `app-temp/data/validation/runs/`.
`api-mongo-e2e`, `layout-live-api` and the other `validation-*`
scripts need the local API (and MongoDB). For an offline E2E without the
microphone, start the API with `PLAYBACK_OFFLINE_TEST=yes` and set
`PLAYBACK_E2E_SKIP_CAPTURE=yes` for `api-mongo-e2e.check.mjs`.
