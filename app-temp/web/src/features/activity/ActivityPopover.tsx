import { useRef, useState } from "react";
import type { Activity, Evidence, Session } from "../../lib/backend/schemas";
import { ActivityLog } from "./ActivityLog";
import { formatElapsed } from "../../lib/time";
import { ActivityEntry } from "./ActivityEntry";
import { useDismiss } from "../../components/Menu";
import { IconButton } from "../../components/IconButton";
import { Chip } from "../../components/Chip";

// Running and queued calls are purple, failed ones red, finished ones muted.
function statusColor(status: string) {
  if (status === "running" || status === "queued") return "text-[#6845bb]";
  if (status === "failed") return "text-[#b33242]";
  return undefined;
}

export function ActivityPopover({
  session,
  items,
  error,
  onSource,
  onNotesRestored,
  compact = false,
}: {
  session: Session | null;
  items: Activity[];
  error?: string;
  onSource: (source: Evidence) => void;
  onNotesRestored: () => Promise<void>;
  compact?: boolean;
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
      className="relative inline-flex"
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
      <IconButton
        small={compact}
        className={compact ? "rounded-full border-0 bg-transparent" : undefined}
        ref={button}
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
      </IconButton>
      {open && (
        // Placed under the button by `position`, kept on screen by its own size limits.
        <section
          id="notes-activity"
          className="fixed z-30 max-h-[min(540px,calc(100dvh-190px))] w-[min(500px,calc(100vw-32px))] overflow-auto overscroll-contain rounded-lg border border-line bg-white p-3.5 text-[12px] shadow-[0_12px_35px_#26305030]"
          style={position}
          aria-label="AI activity history"
          tabIndex={-1}
        >
          <strong>AI activity</strong>
          {error && (
            <p className="mb-3" role="alert">
              {error}
            </p>
          )}
          <div>
            {items.length === 0 && (
              <p className="mb-3">
                No model calls recorded by this API version yet.
              </p>
            )}
            {items.map((item) => (
              <ActivityEntry
                key={item.id}
                title={item.task}
                detail={item.status}
                detailClassName={statusColor(item.status)}
              >
                {() => (
                  <>
                    <p className="mb-3">
                      {item.provider} · {item.model}
                    </p>
                    <small className="text-[smaller]">
                      {new Date(item.startedAt).toLocaleTimeString()} ·{" "}
                      {item.durationMs == null
                        ? "in progress"
                        : `${(item.durationMs / 1000).toFixed(1)}s`}
                    </small>
                    <p className="mb-3">{item.summary}</p>
                    {item.sourceIds.map((id) => {
                      const transcript = session?.transcripts.find(
                        (x) => x.id === id,
                      );
                      const material = session?.materials.find(
                        (x) => x.id === id,
                      );
                      return (
                        <Chip
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
                        </Chip>
                      );
                    })}
                  </>
                )}
              </ActivityEntry>
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
