import type { Evidence, NoteEditLog, Session } from "../types/api";
import { SourceLinks } from "./SourceLinks";
import { formatDateTime } from "./format";
import { api } from "./api";
import { useState } from "react";

type NoteEditHistoryProps = {
  session: Session;
  notes: NoteEditLog[];
  onSource: (source: Evidence) => void;
};

export function NoteEditHistory({
  session,
  notes,
  onSource,
}: NoteEditHistoryProps) {
  const [restoring, setRestoring] = useState(false);
  const [message, setMessage] = useState("");
  if (!notes.length) return <p className="empty">No saved note edits yet.</p>;
  return [...notes].reverse().map((note) => {
    const authorLabel =
      note.author === "user"
        ? "Manual edit"
        : note.author === "agent"
          ? "AI edit"
          : note.author;
    const hasInputs =
      note.inputTranscriptIds.length > 0 || note.inputMaterialIds.length > 0;
    return (
      <details className="activity-entry" key={note.version}>
        <summary>
          <strong>
            v{note.version} · {authorLabel}
          </strong>
          <span>{formatDateTime(note.createdAt)}</span>
          <small>{note.edits.length} changes</small>
        </summary>
        <div className="activity-entry-body">
          <button className="text-control" disabled={restoring || note.version === session.noteVersion}
            onClick={async () => {
              setRestoring(true); setMessage("");
              try { await api(`/sessions/${session.id}/notes/${note.version}/restore`, "POST"); setMessage(`Restored v${note.version} as a new version. Unsaved editor text is retained.`); }
              catch(error) { setMessage(error instanceof Error ? error.message : String(error)); }
              finally { setRestoring(false); }
            }}>Restore this version</button>
          {message && <p role="status">{message}</p>}
          <p className="activity-help">
            {note.basedOnVersion == null
              ? "First saved version"
              : `Based on v${note.basedOnVersion}`}{" "}
            · {note.author}
          </p>
          {hasInputs && (
            <div>
              <strong className="activity-label">Inputs to this update</strong>
              <SourceLinks
                session={session}
                transcriptIds={note.inputTranscriptIds}
                materialIds={note.inputMaterialIds}
                onSelect={onSource}
              />
            </div>
          )}
          {note.edits.length === 0 && (
            <p className="empty">No recorded line changes in this version.</p>
          )}
          {note.edits.map((edit, index) => (
            <div className="activity-edit" data-kind={edit.kind} key={index}>
              <small>
                {edit.kind === "insert" ? "Added" : "Removed"} · line{" "}
                {edit.line}
              </small>
              <pre>{edit.text || "(blank line)"}</pre>
              <SourceLinks
                session={session}
                transcriptIds={edit.transcriptIds}
                materialIds={edit.materialIds}
                onSelect={onSource}
              />
            </div>
          ))}
        </div>
      </details>
    );
  });
}
