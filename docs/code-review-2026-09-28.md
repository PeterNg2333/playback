# Prototype readability and maintenance review

Reviewed the web UI, state/controller paths, recording source selection, and
saved activity metadata in this change. The OCR CLI was unavailable, so this
was a direct source review without installing tools or calling a review provider.

## Findings resolved

| Finding | Change |
| --- | --- |
| Activity mixed requests, loading state, note rendering, term decisions, and source links. | Keep request state in `ActivityContent`; render note edits and term decisions separately, with shared `SourceLinks`. Guard delayed responses by session ID and effect cleanup. |
| Transcript settings performed API requests inside JSX callbacks. | Move translation retry and term review to controller methods; settings render named actions and derived state. |
| Player menu listeners and UI were embedded in the footer. | Give `PlaybackModeMenu` ownership of the menu, outside clicks, and Escape handling. |
| Old demo styles and repeated override blocks obscured the effective layout. | Remove unused selectors; consolidate selectors and responsive rules in seven files under `web/src/styles/`. |
| A missing capture snapshot selected the active-recording branch. | Treat the initial snapshot as idle; keep Record disabled until the session and health checks are ready. |
| Source navigation assumed React would finish rendering within 30ms. | Reveal and scroll to the source in an effect after the selected Transcript view renders. Discard a pending jump if the session changes. |
| Jev's recorded rule text could drift from its actual thresholds. | Derive the rule text and highlight decision from the same named threshold constants. |

## Verification

- .NET protocol checks and web TypeScript/production build.
- Local API/Mongo integration checks and browser E2E, including capture source
  selection, pause/resume, saved activity, source navigation, and playback.
- Browser fixtures for failure states, legacy API capability handling, and
  layouts down to 320px. Compared computed styles before/after CSS cleanup in
  Transcript, Activity, and Notes at nine widths (27 combinations).

The final E2E run encountered a microphone startup error (`0x80070057`);
the UI displayed it and capture remained idle. System and combined-source
flows continued. The user's separate API was recording during this run and
was left running. This run does not prove every device can open concurrently.

External ASR, Gemini, and Jev provider calls are excluded from these checks.
This review does not establish provider accuracy or three-hour session endurance.
