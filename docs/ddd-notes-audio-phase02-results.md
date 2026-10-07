# Phase 2 implementation results

Date: 2026-10-07. Implemented locally after the Phase 0/1 report and the user's instruction to continue. No commit, staging or push. Phase 3 has not started.

## Review units

| Unit | Main files | Behavior |
| --- | --- | --- |
| Typed settings and persistence | `domain/Notes/ProcessingPolicy.cs`, `application/Notes/ProcessingSettings.cs`, `infrastructure/Notes/MongoProcessingSettingsRepository.cs`, API `Db/ProcessingSettings.cs` and `Endpoints/ProcessingSettingsEndpoints.cs` | Validated server → workspace → session inheritance; explicit false/zero; per-field overrides/reset; schema and resource revisions; strict input; optimistic conflicts including concurrent first writes; server-derived ownership. Separate additive settings collection, no destructive migration. |
| Admission and durable work | `domain/Notes/NoteAdmissionPolicy.cs`, `Note.cs`, `application/Notes/NoteWorkflow.Automatic.cs`, `NoteWorkflow.cs`, `NoteScheduler.cs`, infrastructure note/Jev adapters | Stop/deadline evaluated before unchanged wait; configured thresholds/cadence; persisted first-pending age and policy snapshot; local admission preserves the actual Jev result; bounded retries; settled no-change identity suppresses repeated calls. |
| API/UI delivery | API `Db/Notes.cs`, `SessionSync.cs`, `Activity/AiFlow.cs`, `HealthEndpoints.cs`, `Program.cs`; web `ProcessingSettingsDialog.tsx`, `NoteAdmissionStatus.tsx`, `NotesPanel.tsx`, backend schemas/sync, `AiFlowDialog.tsx` | Notes actions opens settings. Workspace defaults and per-field session overrides, advanced probability/confidence controls, numeric/sliding inputs, inline validation, reset/reload, conflicts retaining the draft. Incremental effective-policy/admission sync, wait countdown, writing/failure/Stop reason, actual effective settings in AI-flow diagnostics. |
| Acceptance checks | `ProcessingPolicyChecks.cs`, `NoteApiChecks.cs`, scheduling/baseline/sync fixtures, `web/test/processing-settings.check.mjs` | Real use cases and endpoint delegates run with memory storage/HTTP handlers; real Edge browser renders actual components against fixture API responses. No provider, database or recording host is used. |

The Phase 0/1 extraction stays in place. The original flat-note, small-dirty-organization and quiet-audio baselines remain for their later phases. Only the stalled-wait baseline is replaced by Phase 2 deadline acceptance. The existing unrelated translation edit remains exactly `targets.First().SessionId`.

## Observable acceptance

| Scenario | Result |
| --- | --- |
| Jev waits indefinitely | Configured two-second checks and ten-second maximum age: one Jev call, no writer before age ten seconds, one writer at the first eligible deadline tick after restart. Saved decision remains `wait`; admission reason is `deadline`. Short English speech works without a character minimum. |
| Threshold change/restart | Synthetic update probability 0.55/confidence 0.26 waits at defaults 0.70/0.50. Session thresholds 0.50/0.20 invalidate the persisted decision, then admit one writer after restart. This validates configuration, not natural provider quality. |
| Gate disabled | One writer at the configured cadence, zero Jev requests, reason `gate-disabled`. |
| Filler/all-deferred reply | One gate and one writer request, no empty note version, retained deferred source. Twelve later ticks plus restart produce no further provider calls. No-op settings saves preserve the policy revision. |
| Jev failure | Real failed fixture Activity retained. A local deadline admits Gemini during gate backoff, labelled `deadline-after-gate-failure`; no fabricated allow or repeated gate request. |
| Writer failures | Backoff and three-attempt limit hold; pending sources remain. Explicit Revise recovers after automatic retries are exhausted. |
| Concurrent Stop and late ASR | A newer Stop arriving during writing survives the old decision save/acknowledgement. Restart retains Stop while a saved chunk awaits ASR. The late final source is written once, then the matching Stop is acknowledged. No Jev call is required. |
| Settings persistence/conflicts | Memory repository restart and BSON round-trip preserve partial inheritance and explicit values. Stale own/inherited revisions reject writes; first-write race has one winner; another workspace is unchanged. Production Mongo uses unique `_id` insert and conditional revision replacement; connected Mongo concurrency remains untested. |
| API contract | Real GET/PUT delegates pass default read, partial false override, workspace inheritance, invalid range 400, unknown field 400, and stale revision 409. Existing note/history/restore/coverage/edit/organization routes continue passing. |
| Session sync | Settings/admission travel as one `processing` delta; no second full lecture read. Existing 2,700-source/200,000-character note bootstrap, fragments, idle-zero-record and one-source delta checks pass. |
| Browser | Actual settings components pass inheritance, partial override/false, conflict draft retention, reload/reset, workspace defaults, range validation, mobile dialog scrolling and countdown. A stale cached-session view after switching scopes was found and fixed. Screenshot inspected at `output/playwright/processing-settings/settings-mobile.png`. |

## Validation and reproduction

All use installed tooling and existing restored packages. No installation or external restore was needed.

```powershell
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
node app-temp/web/test/processing-settings.check.mjs
node app-temp/web/test/refactor-baseline.check.mjs
dotnet app-temp/checks/bin/verification/net10.0/Playback.Checks.dll --sample-audio app-temp/data/test-audio/sampleAudio
npm.cmd --prefix app-temp/web run build
dotnet publish app-temp/api/Playback.Api.csproj --no-restore -c Release -p:UseAppHost=false -o output/refactor-phase02-publish
git diff --check
```

The full offline harness passed all nineteen groups. Week 3 lookup passed 1,266 transcript entries in twenty bounded batches. Browser fixtures passed without external requests; the helper owns and closes its local Vite listener and isolated browser. Frontend typecheck/build and Release publish passed. The frontend still emits the existing Zod annotation and large-bundle warnings. No container was built or started.

## Preservation and remaining boundary

No Mongo connection/query/write, database reset, recording cleanup, audio upload, provider call, original API restart, `.env` inspection/modification, commit or push occurred. New persistence code takes effect when the rebuilt API is run. Existing settings-less documents inherit validated defaults; old notes/transcripts/audio are not rewritten. Session deletion code includes only the new session-scoped settings key when the existing explicit deletion operation is requested; no deletion was executed here.

Silence duration and settle grace are validated/persisted seams for Phase 3, with no UI control claiming they are active. Phase 3 supplies actual speech-boundary events/watermarks and chronological quiet-file combination. Organization controls remain Phase 5. Storage CAS for note-version publication across multiple hosts and worker hosting remain later migration work; settings revisions use Mongo's conditional writes but this run does not prove connected database concurrency.

Natural note quality, live Gemini/Jev/ASR, connected Mongo readback, recording hardware and Docker execution were not tested. The browser result proves the settings flow with fixture responses, not full browser-to-Mongo integration. Admission deadlines exclude provider time and additional retry/backoff; they do not promise completed notes within the configured wait.
