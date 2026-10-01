import type { Evidence, NoteEditLog, Session } from "../../lib/backend/schemas";
import { SourceLinks } from "../sources/SourceLinks";
import { formatDateTime } from "../../lib/time";
import { LazyDetails } from "../../components/LazyDetails";
import { Button } from "../../components/Button";
import { EmptyState } from "../../components/EmptyState";

type NoteEditHistoryProps = {
  session: Session;
  notes: NoteEditLog[];
  restoring: boolean;
  onRestore: (version: number) => void;
  onSource: (source: Evidence) => void;
};

export function NoteEditHistory({
  session,
  notes,
  restoring,
  onRestore,
  onSource,
}: NoteEditHistoryProps) {
  if (!notes.length) return <EmptyState>No saved note edits yet.</EmptyState>;
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
      <LazyDetails
        className="activity-entry"
        data-testid="activity-entry"
        key={note.version}
        summary={
          <>
            <strong>
              v{note.version} · {authorLabel}
            </strong>
            <span>{formatDateTime(note.createdAt)}</span>
            <small>{note.edits.length} changes</small>
          </>
        }
      >
        {() => (
          <div className="activity-entry-body">
            <Button
              disabled={restoring || note.version === session.noteVersion}
              onClick={() => onRestore(note.version)}
            >
              Restore this version
            </Button>
            <p className="activity-help">
              {note.basedOnVersion == null
                ? "First saved version"
                : `Based on v${note.basedOnVersion}`}{" "}
              · {note.author}
            </p>
            {hasInputs && (
              <div>
                <strong className="activity-label">
                  Inputs to this update
                </strong>
                <SourceLinks
                  session={session}
                  transcriptIds={note.inputTranscriptIds}
                  materialIds={note.inputMaterialIds}
                  onSelect={onSource}
                />
              </div>
            )}
            {note.edits.length === 0 && (
              <EmptyState>No recorded line changes in this version.</EmptyState>
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
        )}
      </LazyDetails>
    );
  });
}
