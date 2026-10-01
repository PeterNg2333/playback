# Prototype code structure

## Web

`web/src/main.tsx` mounts `app.tsx`. There is one page, `pages/PlaybackPage.tsx`.
Feature-specific panels, navigation content, recording controls, handlers,
and the Zustand store live directly in `pages/`. Reusable shells live in
`Component/Layout/`:

- `Header.tsx` renders the header shell; the playback controls are supplied by
  `pages/PlaybackHeader.tsx`.
- `SideNav.tsx` renders the navigation shell; session and group entries stay in
  `pages/SessionNav.tsx` and `pages/SessionItem.tsx`.
- `Panel.tsx` and `Workspace.tsx` provide outer structure only.

`Component/Dialog/TextInputDialog.tsx` uses the browser's modal `<dialog>` for
session and group names. `Component/Icon.tsx` and `Component/Markdown.tsx`
have no session-specific requests. `types/api.ts` holds Zod response and input
schemas; `pages/api.ts` applies them at the fetch boundary. Browser session
data is refreshed from the local API and is not persisted by Zustand.
The sidebar lists groups as folders and ungrouped items in Sessions. Sessions
can be moved by dragging to a group or back to Sessions; each session also has
a keyboard-accessible move menu. Deleting a group keeps its sessions and moves
them to Sessions through `DELETE /api/groups/{id}`.

`TranscriptPanel.tsx` renders the transcript. `ActivityPopover.tsx` sits beside
Lecture notes and reads bounded execution state via `useActivity.ts`.
`ActivityContent.tsx` owns loading saved edits from `/notes/edits`.
`NoteEditHistory.tsx` and `TermDecisionTrace.tsx` render the two histories;
restoring a saved note creates a new version. `Services/Ai/AiActivity.cs` owns
execution records; it does not replace saved note versions or Jev decisions.
Notes polling requests `activity?includePrompt=false` and keeps draft/identity/usage
metadata; group flow retains the full effective prompts. Response projection does
not mutate saved records or live activity objects.
`SourceLinks.tsx` supplies their shared source links. Loading, loaded, and failed
states carry the session ID, so a delayed response cannot replace another
session's history. Source navigation runs after React renders the Transcript view.
`TranscriptSettings.tsx` renders settings; API operations remain in `handlers.ts`.
`NotesPanel.tsx` owns Preview/Edit/Live draft, Reading/Sources, draft base conflicts and the topic tree. `NoteTools.tsx` owns paged/direct history, recovery previews and selected-section organization. `GroupFlow.tsx` reads session-scoped configuration and recorded executions from the group menu. `Markdown.tsx`
transforms reference text through the Markdown AST; `SourceCitation.tsx` shows
grouped audio passages without changing saved chunk IDs. `termSegments.ts` is
shared by notes and transcripts; `TermHighlight.tsx` and `TermExplanation.tsx`
show already saved Jev-selected explanations without generating on hover.
`ChatConversationMenu.tsx` organizes lecture sessions by group;
`useChatConversations.ts` loads session-scoped saved conversations;
`ChatAnswer.tsx` renders their validated answers with collapsed source details.
`PlaybackFooter.tsx` keeps the player in two rows. `PlaybackModeMenu.tsx` owns
the scope/source menu and its outside-click and Escape behavior.
`PlaybackHeader.tsx` chooses the recording source independently, using the API's
`recordingSourceSelection` capability to avoid offering unsupported modes.

`styles/` separates shared defaults, workspace/navigation layout, notes,
transcript, recording/player controls, activity, and overlays. Responsive rules
sit after the base rules in each file. `app.tsx` imports them in that order;
feature styles can override shared controls without a separate override sheet.

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
  completions before partial results become saved notes or answers.
- `Db/`: MongoDB models and partial `PlaybackStore`; `Conversations.cs` owns
  the new session-scoped conversations/conversation_turns collections.
  `SectionNotes.cs` owns note versions, paged/direct history, recover/restore and
  persisted note gates; no destructive migration is required. `SessionSync.cs`
  sends bounded record deltas/fragments and keeps at most 32 short-lived reader cursors.
  Bootstrap/display-language reconfiguration reads a full session. Later revisions
  query changed record IDs and affected terms, including saved explanation provenance;
  idle polling makes no record queries. The change journal/cursors are in memory:
  restart, expiry or a journal overrun resets bootstrap rather than claiming a durable DB cursor.

`pages/VirtualTranscript.tsx` mounts only visible variable-height rows and overscan,
including day/hour toggles and source reveal for unmounted rows. `sessionSync.ts`
assembles paged deltas and reuses unchanged objects. `handlers.ts` coordinates
full/snapshot single flights, aborts stale loads, and retains the editor's actual
base version. Timeline/term/Markdown/audio indexes are reused; level/clock-only
updates do not rebuild the whole transcript. Original ASR/source identities remain intact.
`transcriptPassages.ts` / `TranscriptPassage.tsx` optionally consolidate mature
same-source display passages with original parts, translations, selection and queued audio.
Session loading is visible, disables Save and preserves drafts after a failed read.

The scroll owner is `.transcript-content`; the outer `.transcript-view` is not a
scroller. Interim changes reuse the lecture layout and update visible rows.
`Component/diagramRenderer.ts` owns each temporary Mermaid host, bounded jobs and
a count/byte-limited SVG cache. `LazyDetails.tsx` mounts expensive history bodies
only while expanded. NotePreview shares a citation index across sections and
resets it at the session boundary. API request scopes dispose timers/listeners;
capture finals do not load an unselected recording. Evidence and remaining
long-duration/runtime limits: [frontend memory report](../docs/frontend-memory-validation.zh-HK.md)
and [Week 3 one-hour validation](../docs/week3-one-hour-validation.zh-HK.md).
Mermaid is imported only for the first diagram; its pending/error/loading state
is visible. Output guards collect reported usage before rejecting incomplete responses,
including usage emitted after the SDK streaming finish reason.

Each folder also has a matching `Playback.Api.*` namespace. These namespaces
make the C# module boundary visible; the injected service and store classes
contain behavior, while endpoint classes register HTTP routes.

Microsoft's [.NET project organization guidance](https://learn.microsoft.com/en-us/dotnet/core/tutorials/libraries)
allows projects to be arranged to suit the solution. The
[Minimal API documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0)
describes grouping related routes, and the
[ASP.NET Core dependency injection guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
supports keeping application behavior in injected services.

The original layout refactor did not change routes or collections. The later
notes/chat update adds conversation routes and collections; it does not clear
or destructively migrate local data. See [current behavior](docs/notes-chat-update.zh-HK.md) and [redesign verification and limits](../docs/notes-redesign-validation.zh-HK.md).
Offline verification:

`checks/week3-server.mjs` starts isolated offline/live validation ports with automatic
paid work disabled. `Week3StoreCheck.cs` retains exact first-hour text provenance in
`playback_e2e`; `run-week3-live.mjs` and `run-week3-term.mjs` require explicit live opt-in
and reuse saved results. `week3-report.mjs` combines saved usage from successful and
failed calls without duplicating execution IDs. Its optional localhost snapshot is GET-only.
`web/test/week3-e2e.check.mjs` distinguishes paid phases from saved read/restart phases.

```powershell
dotnet build app-temp/api/Playback.Api.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
npm.cmd --prefix app-temp/web run build
```

`dev.mjs` checks that the NuGet package files named in the ignored `obj/` restore
assets still exist before using `dotnet run --no-restore`. This catches restore
assets left by another Windows account or sandbox. For an offline browser E2E
that avoids recording from the local microphone, start the local API with
`PLAYBACK_OFFLINE_TEST=yes` and the web server on port `5174`, then set
`PLAYBACK_E2E_SKIP_CAPTURE=yes` when running `web/test/api-mongo-e2e.check.mjs`.
For a sidebar-only check without MongoDB, run the web server on port `5174`
and then `node app-temp/web/test/library-sidebar.check.mjs`; it intercepts API
requests in the browser and uses disposable fixture data.
