import { LazyDetails } from "../../components/LazyDetails";
import { Chip } from "../../components/Chip";
import { EmptyState } from "../../components/EmptyState";
import type { Evidence, Session } from "../../lib/backend/schemas";

// The lines the current version added or removed, each with chips for the transcript lines it cites.
export function VersionChanges({
  session,
  onOpenSource,
}: {
  session: Session;
  onOpenSource: (source: Evidence) => void;
}) {
  const edits = session.currentNote?.edits ?? [];
  return (
    <LazyDetails
      className="mt-3 border-t border-line pt-2.5"
      summaryClassName="cursor-pointer text-[12px] font-bold text-muted"
      summary={
        <>
          Changes in v{session.noteVersion} · {edits.length}
        </>
      }
    >
      {() =>
        edits.length ? (
          <ol className="my-2.5 grid list-decimal gap-2.5 pl-5">
            {edits.map((edit, index) => (
              <li className="pl-0.5" key={`${edit.kind}-${edit.line}-${index}`}>
                <small className="text-[11px] text-muted">
                  {edit.kind === "insert" ? "Added" : "Removed"} · line{" "}
                  {edit.line}
                </small>
                <p className="my-0.75 text-[12px] whitespace-pre-wrap wrap-anywhere">
                  {edit.text || "(blank line)"}
                </p>
                {edit.transcriptIds.map((id) => {
                  const source = session.transcripts.find(
                    (entry) => entry.id === id,
                  );
                  return (
                    source && (
                      <Chip
                        key={id}
                        onClick={() => onOpenSource({ kind: "lecture", id })}
                      >
                        {String(source.startMs / 1000) + "s"}
                      </Chip>
                    )
                  );
                })}
              </li>
            ))}
          </ol>
        ) : (
          <EmptyState>No recorded line changes in this version.</EmptyState>
        )
      }
    </LazyDetails>
  );
}
