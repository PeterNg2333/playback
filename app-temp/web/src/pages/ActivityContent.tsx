import { useEffect, useState } from "react";
import type { Evidence, NoteEditLog, Session } from "../types/api";
import { NoteEditLogSchema } from "../types/api";
import { api } from "./api";
import { NoteEditHistory } from "./NoteEditHistory";
import { TermDecisionTrace } from "./TermDecisionTrace";

type HistoryState =
  | { status: "loading"; sessionId: string }
  | { status: "loaded"; sessionId: string; notes: NoteEditLog[] }
  | { status: "failed"; sessionId: string; message: string };

type ActivityContentProps = {
  session: Session | null;
  onSource: (source: Evidence) => void;
};

export function ActivityContent({ session, onSource }: ActivityContentProps) {
  const [history, setHistory] = useState<HistoryState | null>(null);
  const sessionId = session?.id;
  const noteVersion = session?.noteVersion;

  useEffect(() => {
    if (!sessionId) return;
    let active = true;
    setHistory({ status: "loading", sessionId });
    api(
      `/sessions/${sessionId}/notes/edits`,
      "GET",
      undefined,
      NoteEditLogSchema.array(),
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
        onSource={onSource}
      />
    );
  }

  return (
    <div className="activity-content">
      <p className="activity-intro">
        Read-only · saved edits and decisions for this session.
      </p>
      <section aria-labelledby="llm-log-title">
        <h3 id="llm-log-title">LLM edit log</h3>
        <p className="activity-help">
          AI and manual note versions, newest first. Expand a version to inspect
          its changes and sources.
        </p>
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
