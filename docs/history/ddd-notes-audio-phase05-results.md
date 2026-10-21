# Phase 5 results: organization and reviewed rewrites

Date: 2026-10-08. Implemented and verified offline.

Organization settings now inherit from workspace defaults with session overrides: enable/disable, meaningful changed revisions, maximum dirty age, cooldown, affected-topic/document-outline scope and note presentation. Explicit `PLAYBACK_AUTO_ORGANIZE=no` remains an inherited disabled default. Revision/age admission replaces the legacy document-size rule. Persisted jobs retain input/policy identity, pending scope, retry/backoff and an acknowledgement checkpoint. Restart recognizes an already committed organization. One organization job may run alongside ordinary generation; pending speech has priority.

Outline operations send compact topic metadata instead of lecture prose. Safe structural operations retain block bodies, citations and coverage identities. Body rewrite/adjacent merge proposals store original bodies and explicit retained-block mappings. Formula, code, number, condition, uncertainty, example and citation checks can block acceptance. Passing mechanical checks does not prove meaning: acceptance requires explicit semantic review, the original document version and unchanged bodies. Accepted merges retain historical block identities. The old fallback that appended lost prose at the section end has been removed; a failed retention proposal leaves the existing document in place.

The browser exposes organization settings/status and a Before/Proposed rewrite dialog. Failed/stale acceptance retains the preview, and unsaved editor changes prevent acceptance. Existing history remains immutable; accepted rewrites append a protected version.

Validation:

- Full offline harness: 22 groups passed, including fixed-clock organization admission/cooldown, small notes, restart acknowledgement, inherited disable, a 180,000-character Chinese legacy body, generation priority, rejected rewrites and explicitly accepted retry-safe proposals.
- Actual API delegates in memory: rewrite listing, rejection without semantic opt-in, successful acceptance and existing notes/history/editor routes passed.
- Actual Edge/React fixture checks: organization controls/inheritance/cross-field bounds and rewrite math/semantic opt-in/retention blocking/stale version/draft protection/mobile width passed. No database or provider calls.
- Frontend TypeScript/Vite build passed; existing bundle-size warnings remain.

No running recording was restarted, no original audio or database data was changed, and no live provider request was made. Phase 6 still needs to finish the remaining backend boundaries and hosted worker lifecycle; Phase 7 requires separate database/live acceptance evidence.
