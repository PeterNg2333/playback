import type { Evidence, NoteEditLog, Session } from "../../lib/backend/schemas";
import { SourceLinks } from "../sources/SourceLinks";
import { formatDateTime } from "../../lib/time";
import clsx from "clsx";
import { ActivityEntry, ActivityHelp } from "../activity/ActivityEntry";
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
      <ActivityEntry
        key={note.version}
        title={`v${note.version} · ${authorLabel}`}
        detail={formatDateTime(note.createdAt)}
        aside={`${note.edits.length} changes`}
      >
        {() => (
          <div className="px-3 pb-3">
            <Button
              disabled={restoring || note.version === session.noteVersion}
              onClick={() => onRestore(note.version)}
            >
              Restore this version
            </Button>
            <ActivityHelp>
              {note.basedOnVersion == null
                ? "First saved version"
                : `Based on v${note.basedOnVersion}`}{" "}
              · {note.author}
            </ActivityHelp>
            {hasInputs && (
              <div>
                <strong className="text-[11px]">Inputs to this update</strong>
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
              // Added lines are green, removed lines red.
              <div
                className={clsx(
                  "mt-2 border-l-2 px-2.5 py-1.75",
                  edit.kind === "insert"
                    ? "border-[#4c956d] bg-[#f3faf5]"
                    : "border-[#c55c6c] bg-[#fff5f6]",
                )}
                key={index}
              >
                <small className="text-[10px] text-muted">
                  {edit.kind === "insert" ? "Added" : "Removed"} · line{" "}
                  {edit.line}
                </small>
                <pre className="my-1 font-[inherit] text-[12px] leading-[1.55] whitespace-pre-wrap wrap-anywhere">
                  {edit.text || "(blank line)"}
                </pre>
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
      </ActivityEntry>
    );
  });
}
