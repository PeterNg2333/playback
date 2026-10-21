# Phase 7 acceptance evidence and remaining decisions

Date: 2026-10-08. Offline acceptance passed; integrated live/database acceptance is pending explicit opt-in.

| Evidence | Result |
| --- | --- |
| Full offline .NET harness | 25 groups passed, fixture storage/providers only |
| Three-hour virtual lecture | Passed: two sources, 2,700 cited transcripts, 45 bounded generations, literal body retention, restart/Stop acknowledgement, late-source Ask, paged sync/idle and stale edit protection; about 19 seconds locally |
| Repository Week 3 transcript | Passed read-only: 1,266 source entries, 20 bounded batches and beginning/middle/end retrieval/times |
| Edge/React fixtures | Settings/inheritance/conflicts/cross-field organization bounds, rewrite semantic opt-in/blocked/stale/draft behavior, hierarchical/math preview/editor/outline, chronological quiet rows and original citation playback passed |
| Frontend TypeScript/Vite build | Passed; existing bundle-size warnings remain |
| API publish | Passed with cached packages; deployment runtime untested |
| Note crash/CAS | Both crash points and competing writers passed through fixture ports; real Mongo run pending |
| Original recordings/database | No original recording or database write performed; active host was not restarted |
| Natural lecture quality/live ASR | Not tested in this refactor run |
| Three-hour physical recording / deployed Linux/Cloud Run | Not tested; virtual time/build evidence does not establish hardware or deployment behavior |

The next database check is `--refactor-mongo`, guarded by `PLAYBACK_REFACTOR_MONGO=yes`. It requires an existing localhost Mongo service, writes only fresh synthetic data to `playback_e2e`, injects a failure after head commit, verifies fresh-repository recovery and competing CAS, retains synthetic data, and reads original database counts without modifying that database. The runner also exercises 125-version history and 900-source sync, then starts its own API on 5082 with a separate audio/key folder and synthetic credentials. Its prepared Edge check covers two owners, anonymous/foreign rejection, cited hierarchy with literal body retention, settings UI/inheritance/conflicts, isolated API restart, history/restore and mobile readback. None of these real-Mongo/browser checks has been executed in this run. It starts no container and clears no collection.

After explicit opt-in and the normal offline build commands, run from the repository root:

```powershell
# Existing localhost Mongo only; all provider work disabled.
node app-temp/checks/Validation/run-refactor-acceptance.mjs --allow-local-mongo
# Adds the first 20 minutes of repository audio: at most 40 ASR and 13 Gemini calls.
node app-temp/checks/Validation/run-refactor-acceptance.mjs --allow-local-mongo --allow-live-sample
```

The runner requires the built web `dist` and .NET `bin/verification/net10.0` outputs. It refuses an occupied test port, disables saved ASR recovery and background translation/notes/terms/organization, and stops only the processes it created. Results/audio remain in `output/refactor-acceptance/<timestamp>` and synthetic database sessions remain in `playback_e2e`. Live generation saves per-status source counts and fails if its bounded call budget leaves pending/failed sources. Deferred/suppressed counts and a nonempty note still require semantic review.

The live sample must run on a separate API port/data root, with saved-ASR recovery and unrelated background provider work disabled. Use the repository’s `sampleAudio.m4a` (first 20 minutes) and preserve the new run/session for review. Review hierarchy, conditions, formulas/examples, uncertainty and citations against the source before accepting natural note quality. A live-test instruction is the required opt-in for its network, paid provider and audio-upload steps under [AGENTS.md](../../AGENTS.md); no additional privacy/retention approval is required.

Phase 7 is not complete until the isolated Mongo, browser/API readback/restart and authorized live-source semantic review have actually passed. Existing cached/live records from older runs are separate evidence and do not prove the new prompts/architecture.
