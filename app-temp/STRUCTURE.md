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
| `components/` | Generic UI that knows no feature: layout shells, `Menu`, the name dialog, `Icon`, the Markdown renderer (`markdown/`) | — |
| `lib/` | The API client and response schemas (`backend/`), the query client, browser UI state, health, time formatting | `backend/client.ts` |
| `styles/` | CSS by area, imported in order by `main.tsx` | — |

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

## .NET API

`api/Playback.Api.csproj` is the root of a single .NET web project. The .NET 10
`webapi` SDK template puts the `.csproj` and `Program.cs` directly at the
project root; it does not require an inner `src/` directory. A repository with
several projects may instead use `src/Playback.Api/` and `tests/Playback.Checks/`.
That extra layer is unnecessary for this prototype and would change existing
project paths and study commands.

- `Program.cs`: host, dependency injection, middleware, and route registration.
- `Endpoints/`: HTTP mapping and request contracts. Each resource has a small
  mapping class. Minimal API routes are commonly registered by static methods;
  service and persistence work uses injected C# classes.
- `Middleware/`: shared HTTP error handling.
- `Services/Ai/Providers/`: `GeminiLanguageModel` sends generation and grounded
  search requests; `JevTermClassifier` owns the Jev ranking request and its
  question patterns. `Services/ProviderResponseReader.cs` bounds all provider
  response bodies.
- `Services/Ai/Agents/`: `TranslationAgent` polls and translates pending
  transcripts using `TranslationContext`; `NoteAgent` generates and retries
  notes; `ChatAgent` answers questions using `ChatContext` to select source
  evidence. `SyntheticTermComparison` runs only the labeled synthetic example.
- `Services/Audio/`: `WindowsAudioCaptureService` records microphone and system
  audio, finalizes WAV chunks, and replays chunks left on disk. `AsrQueue`
  schedules and retries saved chunks; `AsrProcessor` transcribes one chunk
  through `IAsrAdapter` (SenseVoice or OpenRouter transcription REST).
  `IStreamingAsrAdapter` defines the future realtime lifecycle; capture currently
  sends saved chunks through REST. `AudioSilence` and `AudioActivity` inspect WAV
  data without sending it to an external provider.
  `CaptureSourceModes` allowlists microphone, system, and both; the capture service
  retains the selected mode through pause/resume and opens only those devices.
- `Terms/TermCandidateExtractor.cs`: deterministic regex extraction from
  transcripts and materials. It does not call AI; Jev ranking is separate.
- `Services/Ai/SourceReferences.cs`: prompt-local sequential aliases and bounded
  adjacent transcript groups, expanded to canonical IDs before saving.
- `Services/Ai/NoteSections.cs`: one note document's stable sections, written points,
  persistent citation identities, coverage and protected user edits/deletions/recovery.
  `MaterialSources.cs` supplies immutable bounded material passages. `NoteAgent`
  sends bounded section patches, not a whole-note replacement; the distinct organizer
  task uses the same note/history and validates its selected section/base.
  SDK response schemas derive from the same patch type. Points are the canonical
  Markdown body; code renders citations and retains captured concurrency versions.
  Unaddressed input stays pending, and explicit deferrals retain their reasons.
- `Services/Ai/NoteScheduler.cs` checks every ten seconds without awaiting provider
  work. `Providers/JevNoteGate.cs` owns note-specific structured questions;
  `JevTransport.cs` shares bounded HTTP/discovery with term ranking. `AiFlow.cs`
  derives prompt/configuration descriptions from the actual runtime definitions;
  `AiActivity.cs` records execution identities, prompts and reported usage/latency.
- `Services/Ai/Providers/OutputGuardChatClient.cs`: rejects token-limit
  completions before partial results become saved notes or answers, after
  collecting the usage they report (including usage sent after the streaming
  finish reason).
- `Db/`: MongoDB models and partial `PlaybackStore`; `Conversations.cs` owns
  the new session-scoped conversations/conversation_turns collections.
  `SectionNotes.cs` owns note versions, paged/direct history, recover/restore and
  persisted note gates; no destructive migration is required. `SessionSync.cs`
  sends bounded record deltas/fragments and keeps at most 32 short-lived reader cursors.
  Bootstrap/display-language reconfiguration reads a full session. Later revisions
  query changed record IDs and affected terms, including saved explanation provenance;
  idle polling makes no record queries. The change journal/cursors are in memory:
  restart, expiry or a journal overrun resets bootstrap rather than claiming a durable DB cursor.

Each folder also has a matching `Playback.Api.*` namespace. These namespaces
make the C# module boundary visible; the injected service and store classes
contain behavior, while endpoint classes register HTTP routes.

Microsoft's [.NET project organization guidance](https://learn.microsoft.com/en-us/dotnet/core/tutorials/libraries)
allows projects to be arranged to suit the solution. The
[Minimal API documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0)
describes grouping related routes, and the
[ASP.NET Core dependency injection guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
supports keeping application behavior in injected services.

## Checks

.NET: `checks/Playback.Checks.csproj` runs the offline protocol, ASR, language
and notes checks over in-memory HTTP. `checks/week3-server.mjs` starts isolated
offline/live validation ports with automatic paid work disabled.
`Week3StoreCheck.cs` keeps exact first-hour text provenance in `playback_e2e`;
`run-week3-live.mjs` and `run-week3-term.mjs` need an explicit live opt-in and
reuse saved results; `week3-report.mjs` combines saved usage without counting an
execution twice. `dev.mjs` checks that the NuGet files named in the ignored
`obj/` restore assets still exist before `dotnet run --no-restore`.

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
