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

`TranscriptPanel.tsx` switches between Transcript and a read-only Activity tab.
`ActivityContent.tsx` owns loading saved edits from `/notes/edits`.
`NoteEditHistory.tsx` and `TermDecisionTrace.tsx` render the two histories;
`SourceLinks.tsx` supplies their shared source links. Loading, loaded, and failed
states carry the session ID, so a delayed response cannot replace another
session's history. Source navigation runs after React renders the Transcript view.
`TranscriptSettings.tsx` renders settings; API operations remain in `handlers.ts`.
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
  through `SenseVoiceClient`. `AudioSilence` and `AudioActivity` inspect WAV
  data without sending it to an external provider.
  `CaptureSourceModes` allowlists microphone, system, and both; the capture service
  retains the selected mode through pause/resume and opens only those devices.
- `Terms/TermCandidateExtractor.cs`: deterministic regex extraction from
  transcripts and materials. It does not call AI; Jev ranking is separate.
- `Db/`: MongoDB models and `PlaybackStore`.

Each folder also has a matching `Playback.Api.*` namespace. These namespaces
make the C# module boundary visible; the injected service and store classes
contain behavior, while endpoint classes register HTTP routes.

Microsoft's [.NET project organization guidance](https://learn.microsoft.com/en-us/dotnet/core/tutorials/libraries)
allows projects to be arranged to suit the solution. The
[Minimal API documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0)
describes grouping related routes, and the
[ASP.NET Core dependency injection guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
supports keeping application behavior in injected services.

The refactor does not change routes or database collections and does not clear
or migrate local data. Offline verification:

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
`PLAYBACK_E2E_SKIP_CAPTURE=yes` when running `web/src/test/e2e-check.mjs`.
For a sidebar-only check without MongoDB, run the web server on port `5174`
and then `node app-temp/web/src/test/sidebar-check.mjs`; it intercepts API
requests in the browser and uses disposable fixture data.
