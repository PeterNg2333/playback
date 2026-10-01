import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import type { Evidence, Session } from "../../lib/backend/schemas";
import { NoteEditLogSchema } from "../../lib/backend/schemas";
import { api } from "../../lib/backend/client";
import { NoteEditHistory } from "../notes/NoteEditHistory";
import { TermDecisionTrace } from "../terms/TermDecisionTrace";
import { restoreNoteVersion } from "../notes/restoreNoteVersion";

const EDIT_LOG_TIMEOUT_MS = 30_000;

type ActivityLogProps = {
  session: Session | null;
  onSource: (source: Evidence) => void;
  onNotesRestored: () => Promise<void>;
};

// What was saved for this session: each note version's changes, and Jev's term decisions.
export function ActivityLog({
  session,
  onSource,
  onNotesRestored,
}: ActivityLogProps) {
  const edits = useQuery({
    queryKey: ["noteEdits", session?.id, session?.noteVersion],
    queryFn: ({ signal }) =>
      api.get(
        `/sessions/${session!.id}/notes/edits`,
        NoteEditLogSchema.array(),
        {
          signal: AbortSignal.any([
            signal,
            AbortSignal.timeout(EDIT_LOG_TIMEOUT_MS),
          ]),
        },
      ),
    enabled: !!session,
  });
  const [restoring, setRestoring] = useState(false);
  const [restoreMessage, setRestoreMessage] = useState("");

  async function restore(version: number) {
    if (!session) return;
    setRestoring(true);
    setRestoreMessage("");
    try {
      await restoreNoteVersion(session.id, version, session.noteVersion);
      await onNotesRestored();
      setRestoreMessage(
        `Restored v${version} as a new version. Unsaved editor text is retained.`,
      );
    } catch (error) {
      setRestoreMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setRestoring(false);
    }
  }

  if (!session)
    return <p className="empty">Choose a session to see its activity.</p>;

  let editHistory;
  if (edits.isPending) {
    editHistory = <p role="status">Loading edit history…</p>;
  } else if (edits.isError) {
    editHistory = (
      <p className="capture-error" role="alert">
        Could not load edit history: {edits.error.message}
      </p>
    );
  } else {
    editHistory = (
      <NoteEditHistory
        session={session}
        notes={edits.data}
        restoring={restoring}
        onRestore={restore}
        onSource={onSource}
      />
    );
  }

  return (
    <div className="activity-content">
      <p className="activity-intro">
        Saved edits and decisions for this session. Restoring creates a new note
        version.
      </p>
      <section aria-labelledby="llm-log-title">
        <h3 id="llm-log-title">LLM edit log</h3>
        <p className="activity-help">
          AI and manual note versions, newest first. Expand a version to inspect
          its changes and sources.
        </p>
        {restoreMessage && <p role="status">{restoreMessage}</p>}
        {editHistory}
      </section>
      <section aria-labelledby="jev-trace-title">
        <h3 id="jev-trace-title">Jev decision trace</h3>
        <p className="activity-help">
          Saved term scores and the resulting highlight decision.
        </p>
        <TermDecisionTrace session={session} onSource={onSource} />
      </section>
    </div>
  );
}
