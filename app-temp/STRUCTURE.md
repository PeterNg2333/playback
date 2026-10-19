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
| `features/transcript/` | The transcript timeline: its entries, virtual list, row kinds (`rows/`), ASR status and session settings | `TranscriptPanel.tsx` |
| `features/player/` | Playback of saved audio through one `<audio>` element | `useAudioPlayer.ts` |
| `features/materials/` | Teaching materials: list and attach | `MaterialsList.tsx` |
| `features/notes/` | Reading, editing and saving notes; history, recovery, organizing and coverage; persisted timing/Jev settings and admission status | `NotesPanel.tsx`, `ProcessingSettingsDialog.tsx` |
| `features/terms/` | Key-term highlights, saved explanations, Jev decisions | `TermHighlight.tsx` |
| `features/ask/` | Questions about the session, answers and saved conversations | `AskPanel.tsx` |
| `features/activity/` | The AI execution log and a group's AI flow | `ActivityPopover.tsx` |
| `features/sources/` | Citations: the AI's citation syntax, source chips and links, jumping to a source | `CitedMarkdown.tsx`, `useRevealSource.ts` |
| `components/` | Generic UI that knows no feature: layout shells, shared controls (`Button`, `IconButton`, `Dialog`, `Menu`, …), `Icon`, the Markdown renderer (`markdown/`) | — |
| `lib/` | The API client and response schemas (`backend/`), the query client, browser UI state, health, time formatting | `backend/client.ts` |
| `styles/` | `app.css`: Tailwind, theme tokens and page globals; `markdown.css`: rendered Markdown | `app.css` |

### State

- Data read from the API lives in the TanStack Query cache (`lib/queryClient.ts`),
  keyed by session where it belongs to one: health (`lib/useHealth.ts`), recorder
  status (`recording/captureQuery.ts`, polled every 250 ms by `useCapture`), the
  session list, groups and one session (`library/libraryQueries.ts`; the shown
  session is re-read every 4 s by `useSelectedSession`), AI activity, saved
  conversations, note history, coverage, the AI flow and the edit log.
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
| `domain/` | Evidence/workspace/assistance state, note tree and mutation, citations, retention, timing/organization policies and shared validation | .NET only; no packages |
| `application/` | Notes, transcription, translation context/workflow, terms, Ask, activity, workspace/recording/chunk commands and purpose-specific repository/provider/query ports | Domain only |
| `infrastructure/` | Mongo storage/BSON mappings, Gemini/Agent Framework, OpenRouter text and ASR, Jev, VAD/WAV/WASAPI/native display, bounded sync and hosted workers | Application/Domain; unchanged provider/audio/storage packages |
| `api/` | HTTP request mapping, authentication/CSRF/ownership middleware, static hosting and composition; translation host remains here while its preserved WIP awaits commit approval | Application and Infrastructure |

The migrated public CLR names retain some `Playback.Api.*` namespaces for existing callers. They live in the new assemblies; executable dependency/model ownership checks prevent a hidden project reference back to the host. Compatibility adapters contain delegation, with one active rule/use-case implementation.

| Flow | Path |
| --- | --- |
| Recording | `RecordingCommands` authorizes/control transitions through `IRecordingControl`; WASAPI retains per-source original WAVs and durable stream identity |
| Saved transcription | Hosted `AsrQueue` invokes application `Transcription`, with audio preparation, provider and repository ports; confirmed originals and provider identity remain separate from display/translation |
| Quiet audio | `CombinedAudioWorkflow` + `QuietAudio` plan bounded stream-compatible manifests; infrastructure streams/validates/publishes WAV payloads and original hashes |
| Notes | `NoteWorkflow` supplies bounded evidence/outline, validates provider proposals and saves through `INoteRepository`; `NoteCommands` handles edits/history/recovery and accepted suggestions. The writer is an `INoteWriter` chosen once by `NoteWriters` (`Notes/NoteProviderAdapters.cs`): `PLAYBACK_NOTE_MODEL` (`openrouter:<model>[@reasoning]` or `vertex:<model>`), else Qwen3.8 Flash on OpenRouter when its key exists, else the Gemini model. Every writer sends the caps and schema in `Providers/ModelOutputContract.cs`; chat, translation and term explanations stay on Gemini |
| Note input | `NoteInput` puts the whole lecture first, in time order with stable aliases (`T1`…), then the notes and this call's task, so each call repeats the previous prefix (cacheable where the provider caches). Chinese text is not escaped. Jev gets a separate compact input (`NoteInput.Gate`): the speech just before the passage and the passage |
| Note placement | Like an author choosing headings (`tree-notes-v14`), a reply first declares new topics in `outline` (keys `new1`–`new8`, a noun-phrase title and a parent) and then places each block in `blocks`, naming an open topic or a key. The schema only offers open topics, and as parents only topics with room for another level that were spoken in the last five minutes (`NoteStructure.MaxParentGapMinutes`), so placement is a required decision; to continue a closed topic the writer declares a subtopic under it. Every operation must name its topic and block. The organizer (`tree-organize-v16`) declares its new topics the same way and places existing blocks in `moves` (`{block, to}`) and topics in `topicMoves` (`{topic, to}`); its operations only rename and reorder. The schema requires `to` and offers it only among the selected topics and the new keys, so a destination cannot be missing or outside the scope (Qwen left out `move-block`'s `topicId` or named the block's current topic, and left out `move-topic`'s `parentId` in every regroup). `ExpandPlacements` turns the plan into tree operations; a new or moved block goes after its topic's last block. An organizer operation that still cannot apply is dropped, not fatal, and counted by cause (outside the scope, no destination, already there, deeper than three levels) |
| Note script | For Traditional notes, `Notes/OpenccNoteScript.cs` converts Simplified characters in new bodies, titles and replacements with OpenCC `s2hk` (OpenccNetLib) before they apply. Only Simplified-only characters change (OpenCC's candidate lists plus a short list of characters modern Traditional never uses), and a shared character next to one; correct Traditional such as 干擾, 公里, 游泳 or 着 stays. A word made only of shared characters (示范) is left as written |
| Note edits | Changes to existing notes are editor-style operations. `NoteWorkflow.Edits.cs` applies them one by one: additions, moves, renames and new topics apply at once; a `replace-text` applies when it is small and keeps every number, formula and condition, and a deletion when at most two blocks go and their sources stay cited elsewhere (`NoteEditPolicy`); anything else becomes a suggestion. A source may be deferred once. A block a reply still aims at a closed topic (`NoteStructure`: six blocks, or eight minutes of lecture) goes into the subtopic the same reply declared for it; without one it stays there for the organizer to split. The application never titles a topic itself. After every AI change sibling topics are put in lecture order and emptied topics are removed. A suggestion stays valid until its own block changes and can be dismissed |
| Note admission and organization | Without Jev (the default) a batch is written 30 s after its first new speech, at a speech pause or at Stop. Organization follows structure, not time: a changed topic over six blocks is split into subtopics (with its existing subtopics in scope, up to 80 moves and operations), and a level over six topics that can still take a parent (above the third level) is regrouped from the outline (titles only; it may move, reorder and rename topics). That input names the crowded level and how many levels lie below each topic; depth is at most three, and a move that would go deeper is dropped. The activity summary counts dropped organizer operations by cause. A regrouping that would put a topic under one spoken more than five minutes away is dropped |
| Owner actions | `NoteWorkflow.Instructions.cs`: an instruction for the whole notes, a topic or one block (a comment) returns edit operations applied directly (`POST /notes/instruct`). `NoteCommands.LockBlock` locks a block so no AI path changes it (`POST /notes/block/lock`) |
| Note durability | One expected-version `note_heads` CAS plus pending-history marker; history flush is idempotent and recovered before another head advance, without transactions |
| Organization | Persisted meaningful revision/age/cooldown/scope policy and job checkpoints; one concurrent job, ordinary note generation priority, original bodies retained |
| Terms/Ask | Application `TermReview` / `ChatAgent` use storage/provider ports; domain owns term candidates/ranking rules and source aliases |
| Workspace/auth | Application commands and shared domain validation/credentials; API owns cookie/CSRF/same-origin transport and ownership middleware |
| Page sync | Application query port to the existing bounded projection: paged/fragments first read, record deltas, zero idle records; restart invalidates cursors |

Processing workers are hosted services. Their constructors do not launch scans. Provider execution is outside note write locks; durable head/retry/work markers supply recovery. The prepared translation change preserves the original `targets.First().SessionId` behavior.

Language/style checks use a current policy snapshot plus the existing per-process coordination. Atomic note CAS protects competing note writers; cross-collection settings changes under multiple API hosts still need real acceptance before a horizontal-scaling claim. Windows capture and native Cantonese conversion remain Windows capabilities; a Linux build does not establish their runtime support.

See the [phase plan](../docs/ddd-notes-audio-refactor-plan.md), [Phase 6 results](../docs/ddd-notes-audio-phase06-results.md) and [acceptance evidence](../docs/ddd-notes-audio-phase07-results.md).

## Checks

`checks/Playback.Checks.csproj` is one console project. Read its `Program.cs`
first: it lists every check group and what the group needs. The folders follow
the same question, because it decides whether a check may run now.

| Folder | Needs | How to run |
| --- | --- | --- |
| `Offline/` | Nothing: in-memory HTTP and storage, no network, no database | `dotnet run` with no flag runs all of them; `--notes` runs the note and sync checks; `--sample-audio`, `--sample-audio-preview` and `--vad-sample` read the Week 3 sample, as does `long-lecture-check.py` |
| `LocalMongo/` | MongoDB on localhost, database `playback_e2e` | `--conversation-check`, `--notes-store-check`, `--session-sync-store-check`; `api-integration.check.mjs` also needs the offline API on 5079 |
| `Live/` | Paid providers and an explicit opt-in | `pnpm.cmd test:gemini-live`, `test:jev-live`, `test:asr-live`, `test:sample-audio-live`; `--asr-compare --live` |
| `Validation/` | Writes evidence under `data/validation/runs/` | the `run-*`, `week3-*` and `validation-*` scripts and their flags; offline unless given `--live`. `run-note-models.mjs` compares note writers on the Week 3 transcript (`pnpm.cmd test:note-models-live`) |
| `Fixtures/` | — | `MemoryStore`, the fixture note model and gate, Jev HTTP fakes, `TestClock`, synthetic lectures, the Week 3 sample reader |

Offline check files are named after the product concept they protect
(`AskChecks`, `NoteSchedulingChecks`, …) and print one line when they pass.
`RefactorBaselineChecks` characterizes later-phase problems and legacy BSON/HTTP
readback; `NoteApiChecks` invokes real endpoint delegates entirely in memory;
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
- `notes-citations-chat.check.mjs`: citations, diagrams, topic tree, saved
  terms, chat failures and conversations, layout bounds.
- `notes-reading-sources.check.mjs`: Reading/Sources, 1,350 and 2,700 rows,
  history and recovery, AI flow, edit conflicts and draft recovery.
- `cold-loading.check.mjs` and `markdown-math.check.mjs`: lazy diagram and maths
  loading. `markdown-math` blocks the built `MathFormula-*.js` chunk, so it needs
  a production preview (`MATH_BASE_URL`, default port 5180).

Checks find elements the way a reader does: by role and accessible name, by
label, or by an existing `data-*` hook (`data-transcript-id`, `data-markdown`,
`data-virtual-row`, `data-chunk-id`). An element with no name gets a
`data-testid` named after its component (`audio-row`, `note-content`). They never
select by CSS class, because classes are styling and change with it.

`note-coverage-repair`, `notes-snapshot-performance`, `memory-retention` and
`validation-replay` replay saved runs from `app-temp/data/validation/runs/`.
`api-mongo-e2e`, `layout-live-api`, `week3-e2e` and the other `validation-*`
scripts need the local API (and MongoDB). For an offline E2E without the
microphone, start the API with `PLAYBACK_OFFLINE_TEST=yes` and set
`PLAYBACK_E2E_SKIP_CAPTURE=yes` for `api-mongo-e2e.check.mjs`.
