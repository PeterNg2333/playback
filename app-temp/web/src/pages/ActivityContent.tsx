import { useEffect, useState } from "react";
import type { Evidence, NoteEditLog, Session } from "../types/api";
import { NoteEditLogSchema } from "../types/api";
import { api } from "./api";
import { NoteEditHistory } from "./NoteEditHistory";
import { TermDecisionTrace } from "./TermDecisionTrace";
import { restoreNoteVersion } from "../features/notes/restoreNoteVersion";

type HistoryState =
  | { status: "loading"; sessionId: string }
  | { status: "loaded"; sessionId: string; notes: NoteEditLog[] }
  | { status: "failed"; sessionId: string; message: string };

type ActivityContentProps = {
  session: Session | null;
  onSource: (source: Evidence) => void;
  onNotesRestored: () => Promise<void>;
};

export function ActivityContent({
  session,
  onSource,
  onNotesRestored,
}: ActivityContentProps) {
  const [history, setHistory] = useState<HistoryState | null>(null);
  const [restoring, setRestoring] = useState(false);
  const [restoreMessage, setRestoreMessage] = useState("");
  const sessionId = session?.id;
  const noteVersion = session?.noteVersion;

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

  useEffect(() => {
    if (!sessionId) return;
    let active = true;
    const request = new AbortController();
    setHistory({ status: "loading", sessionId });
    api(
      `/sessions/${sessionId}/notes/edits`,
      "GET",
      undefined,
      NoteEditLogSchema.array(),
      AbortSignal.any([request.signal, AbortSignal.timeout(30000)]),
    )
      .then((notes) => {
        if (active) setHistory({ status: "loaded", sessionId, notes });
      })
      .catch((error) => {
        if (active)
          setHistory({
            status: "failed",
            sessionId,
            message: error instanceof Error ? error.message : String(error),
          });
      });
    return () => {
      active = false;
      request.abort();
    };
  }, [sessionId, noteVersion]);

  if (!session)
    return <p className="empty">Choose a session to see its activity.</p>;

  let editHistory;
  if (
    !history ||
    history.sessionId !== session.id ||
    history.status === "loading"
  ) {
    editHistory = <p role="status">Loading edit history…</p>;
  } else if (history.status === "failed") {
    editHistory = (
      <p className="capture-error" role="alert">
        Could not load edit history: {history.message}
      </p>
    );
  } else {
    editHistory = (
      <NoteEditHistory
        session={session}
        notes={history.notes}
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
