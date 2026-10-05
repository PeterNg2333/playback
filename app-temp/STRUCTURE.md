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
| `features/notes/` | Reading, editing and saving notes; history, restore and recovery; organizing; coverage | `NotesPanel.tsx` |
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

## .NET API

`api/Playback.Api.csproj` is a single web project. The .NET 10 `webapi`
template puts the `.csproj` and `Program.cs` at the project root; an inner
`src/` folder only pays off once there are several projects.

Read `Program.cs` first. It registers the store, the providers, the audio
pipeline and the AI agents in that order, maps one endpoint class per
resource, and resolves the background workers so they start scanning. Every
folder has a matching `Playback.Api.*` namespace.

| Folder | Holds | Start with |
| --- | --- | --- |
| `Endpoints/` | HTTP routes, one class per resource with its request records; error responses | the class named after the resource |
| `Db/` | MongoDB: one `PlaybackStore`, one file per saved concept (sessions, chunks, transcripts, notes, terms, conversations, activity, the page's change feed, test data) | `PlaybackStore.cs` |
| `Audio/Recording/` | Windows microphone and system-audio capture into WAV chunks | `WindowsAudioCaptureService.cs` |
| `Audio/Vad/` | Silero speech detection, while recording and before upload | `SpeechActivityDetector.cs` |
| `Audio/Asr/` | Transcribing saved chunks: the queue, one chunk's processing, the `IAsrAdapter` seam with SenseVoice and OpenRouter in `Providers/`, and interim previews while recording | `AsrQueue.cs`, `IAsrAdapter.cs` |
| `Audio/` | Shared by the folders above: `AudioSamples` reads device buffers; `SessionAudioRenderer` mixes 30-second windows for the player | — |
| `Translation/` | Batched translation of transcripts into the session's target language | `TranslationAgent.cs` |
| `Notes/` | The note document (sections, points, citations, coverage) and the agent that writes it | `NoteAgent.cs` |
| `Terms/` | Key-term candidates, Jev ranking, saved explanations | `TermReviewAgent.cs` |
| `Ask/` | Questions about a session: which sources, which citations count | `ChatAgent.cs` |
| `Sources/` | Short citation aliases and material passages, shared by notes and Ask | `SourceReferences.cs` |
| `Activity/` | The AI execution log and the description of a group's AI flow | `AiActivity.cs` |
| `Providers/` | Gemini through Agent Framework and grounded search; the Jev transport; bounded response reads | `GeminiLanguageModel.cs` |
| `Resources/` | The Silero VAD model and its licence | — |

Three files sit at the root because every folder uses them:
`PlaybackEnvironment.cs` (process switches and data folders),
`LanguageSettings.cs` (session languages and Cantonese display) and
`ContentHash.cs`.

### Flows

- Record: `CaptureEndpoints` → `WindowsAudioCaptureService` writes one
  `.wav.part` per source under `data/local-capture` and closes it after 8 s, or
  after 3 s once speech has paused → `PlaybackStore.SaveLocalChunk`
  (`Db/Chunks.cs`) stores it under `data/audio` by its `ChunkIdentity` →
  `AsrQueue.Enqueue`. Meanwhile `LiveAsrSession` shows interim text that is
  never saved.
- Transcribe: `AsrQueue` (two workers, retries with backoff) → `AsrProcessor`
  skips digital silence and audio without speech, otherwise calls the adapter
  → `SaveTranscript` (`Db/Transcripts.cs`). New text raises `SourceChanged`,
  which queues term review; notes pick it up on their next 10-second check.
- Notes: `NoteAgent.Automatic.cs` runs every 10 s through `NoteScheduler`; the
  Jev note gate answers allow or wait, saved in `note_gates`; an allow calls
  `NoteAgent.Revise`, which builds the bounded input (`NoteInput`), applies
  Gemini's section patch (`NoteSections.Apply`) and saves a new version
  (`SaveSectionNote` in `Db/Notes.cs`). Revise with AI, Organize and coverage
  repair call `Revise` directly.
- Ask: `AskEndpoints` → `ChatAgent` → `ChatContextBuilder` picks and aliases the
  sources → Gemini → the citations are decoded and checked → the turn is saved
  in its conversation.
- Page sync: every write calls `Touch` (`Db/SessionSync.cs`);
  `/sessions/{id}/sync` pages a bounded first read, then sends only changed
  records. The change journal and cursors live in memory, so an API restart
  makes readers start over.

### Rules with one home

- `PlaybackEnvironment`: offline test mode (port 5079, no provider call, no
  background scan), the validation API on 5081, automatic ASR, notes, terms and
  organization, the database and the data folders.
- `ChunkStatus` and `ChunkIdentity` (`Db/Chunks.cs`): the chunk lifecycle, and
  the session/source/sequence/hash identity that makes uploads and retries
  idempotent.
- `NoteInstructions`: the note prompts and their versions. `NoteInput`: what
  the model sees and its size limits.
- Provider calls use fixed endpoints, timeouts and `ProviderResponseReader`'s
  size limit. `AiActivity` records each call and keeps keys out of errors.

Microsoft's [.NET project organization guidance](https://learn.microsoft.com/en-us/dotnet/core/tutorials/libraries)
allows projects to be arranged to suit the solution. The
[Minimal API documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0)
describes grouping related routes, and the
[ASP.NET Core dependency injection guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
supports keeping application behavior in injected services.

## Checks

`checks/Playback.Checks.csproj` is one console project. Read its `Program.cs`
first: it lists every check group and what the group needs. The folders follow
the same question, because it decides whether a check may run now.

| Folder | Needs | How to run |
| --- | --- | --- |
| `Offline/` | Nothing: in-memory HTTP and storage, no network, no database | `dotnet run` with no flag runs all of them; `--notes` runs the note and sync checks; `--sample-audio`, `--sample-audio-preview` and `--vad-sample` read the Week 3 sample, as does `long-lecture-check.py` |
| `LocalMongo/` | MongoDB on localhost, database `playback_e2e` | `--conversation-check`, `--notes-store-check`, `--session-sync-store-check`; `api-integration.check.mjs` also needs the offline API on 5079 |
| `Live/` | Paid providers and an explicit opt-in | `pnpm.cmd test:gemini-live`, `test:jev-live`, `test:asr-live`, `test:sample-audio-live`; `--asr-compare --live` |
| `Validation/` | Writes evidence under `data/validation/runs/` | the `run-*`, `week3-*` and `validation-*` scripts and their flags; offline unless given `--live` |
| `Fixtures/` | — | `MemoryStore`, the fixture note model and gate, Jev HTTP fakes, `TestClock`, synthetic lectures, the Week 3 sample reader |

Offline check files are named after the product concept they protect
(`AskChecks`, `NoteSchedulingChecks`, …) and print one line when they pass.
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
