import { useRef, useState } from "react";
import type { Activity, Evidence, Session } from "../../lib/backend/schemas";
import { ActivityLog } from "./ActivityLog";
import { formatElapsed } from "../../lib/time";
import { LazyDetails } from "../../components/LazyDetails";
import { useDismiss } from "../../components/Menu";

export function ActivityPopover({
  session,
  items,
  error,
  onSource,
  onNotesRestored,
}: {
  session: Session | null;
  items: Activity[];
  error?: string;
  onSource: (source: Evidence) => void;
  onNotesRestored: () => Promise<void>;
}) {
  const [open, setOpen] = useState(false);
  const [pinned, setPinned] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const anchor = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ top: 150, left: 24 });
  const show = () => {
    const rect = button.current?.getBoundingClientRect();
    if (rect)
      setPosition({
        top: rect.bottom,
        left: Math.max(16, Math.min(rect.left - 140, window.innerWidth - 516)),
      });
    setOpen(true);
  };
  const close = () => {
    setOpen(false);
    setPinned(false);
  };
  useDismiss(anchor, open, close);
  return (
    <div
      ref={anchor}
      className="activity-anchor"
      onMouseEnter={show}
      onMouseLeave={() => {
        if (!pinned) setOpen(false);
      }}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) close();
      }}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.stopPropagation();
          button.current?.focus();
          close();
        }
      }}
    >
      <button
        ref={button}
        className="icon-control"
        aria-label="AI activity history"
        aria-expanded={open}
        aria-controls="notes-activity"
        onFocus={show}
        onClick={() => {
          setPinned(!pinned);
          setOpen(!pinned);
        }}
      >
        ↶
      </button>
      {open && (
        <section
          id="notes-activity"
          className="activity-popover"
          style={position}
          aria-label="AI activity history"
          tabIndex={-1}
        >
          <strong>AI activity</strong>
          {error && <p role="alert">{error}</p>}
          <div className="execution-history">
            {items.length === 0 && (
              <p>No model calls recorded by this API version yet.</p>
            )}
            {items.map((item) => (
              <LazyDetails
                className="activity-entry"
                data-testid="activity-entry"
                key={item.id}
                summary={
                  <>
                    <strong>{item.task}</strong>
                    <span data-status={item.status}>{item.status}</span>
                  </>
                }
              >
                {() => (
                  <>
                    <p>
                      {item.provider} · {item.model}
                    </p>
                    <small>
                      {new Date(item.startedAt).toLocaleTimeString()} ·{" "}
                      {item.durationMs == null
                        ? "in progress"
                        : `${(item.durationMs / 1000).toFixed(1)}s`}
                    </small>
                    <p>{item.summary}</p>
                    {item.sourceIds.map((id) => {
                      const transcript = session?.transcripts.find(
                        (x) => x.id === id,
                      );
                      const material = session?.materials.find(
                        (x) => x.id === id,
                      );
                      return (
                        <button
                          className="citation"
                          key={id}
                          onClick={() =>
                            onSource({
                              kind: material ? "material" : "lecture",
                              id,
                            })
                          }
                        >
                          {material?.name ??
                            (transcript
                              ? `${transcript.sourceId} · ${formatElapsed(transcript.startMs)}–${formatElapsed(transcript.endMs)}`
                              : `Source ${id.slice(-8)}`)}
                        </button>
                      );
                    })}
                  </>
                )}
              </LazyDetails>
            ))}
          </div>
          <ActivityLog
            session={session}
            onSource={onSource}
            onNotesRestored={onNotesRestored}
          />
        </section>
      )}
    </div>
  );
}
