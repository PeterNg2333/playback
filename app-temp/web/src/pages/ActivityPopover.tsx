import { useEffect, useRef, useState } from "react";
import type { Activity, Evidence, Session } from "../types/api";
import { ActivityContent } from "./ActivityContent";
import { time } from "./format";

export function ActivityPopover({ session, items, error, onSource }: {
  session: Session | null; items: Activity[]; error?: string; onSource: (source: Evidence) => void;
}) {
  const [open, setOpen] = useState(false);
  const [pinned, setPinned] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const anchor = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ top: 150, left: 24 });
  const show = () => {
    const rect = button.current?.getBoundingClientRect();
    if (rect) setPosition({ top: rect.bottom, left: Math.max(16, Math.min(rect.left - 140, window.innerWidth - 516)) });
    setOpen(true);
  };
  const close = () => { setOpen(false); setPinned(false); };
  useEffect(() => {
    if (!open) return;
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") close(); };
    const outside = (event: PointerEvent) => { if (!anchor.current?.contains(event.target as Node)) close(); };
    document.addEventListener("keydown", escape);
    document.addEventListener("pointerdown", outside);
    return () => { document.removeEventListener("keydown", escape); document.removeEventListener("pointerdown", outside); };
  }, [open]);
  return <div ref={anchor} className="activity-anchor" onMouseEnter={show}
    onMouseLeave={() => { if (!pinned) setOpen(false); }}
    onBlur={(event) => { if (!event.currentTarget.contains(event.relatedTarget)) close(); }}
    onKeyDown={(event) => { if (event.key === "Escape") { event.stopPropagation(); button.current?.focus(); close(); } }}>
    <button ref={button} className="icon-control" aria-label="AI activity history" aria-expanded={open}
      aria-controls="notes-activity" onFocus={show}
      onClick={() => { setPinned(!pinned); setOpen(!pinned); }}>↶</button>
    {open && <section id="notes-activity" className="activity-popover" style={position} aria-label="AI activity history" tabIndex={-1}>
      <strong>AI activity</strong>
      {error && <p role="alert">{error}</p>}
      <div className="execution-history">
        {items.length === 0 && <p>No model calls recorded by this API version yet.</p>}
        {items.map(item => <details className="activity-entry" key={item.id}>
          <summary><strong>{item.task}</strong><span data-status={item.status}>{item.status}</span></summary>
          <p>{item.provider} · {item.model}</p>
          <small>{new Date(item.startedAt).toLocaleTimeString()} · {item.durationMs == null ? "in progress" : `${(item.durationMs / 1000).toFixed(1)}s`}</small>
          <p>{item.summary}</p>
          {item.sourceIds.map(id => {
            const transcript = session?.transcripts.find(x => x.id === id);
            const material = session?.materials.find(x => x.id === id);
            return <button className="citation" key={id}
              onClick={() => onSource({ kind: material ? "material" : "lecture", id })}>
              {material?.name ?? (transcript ? `${transcript.sourceId} · ${time(transcript.startMs)}–${time(transcript.endMs)}` : `Source ${id.slice(-8)}`)}
            </button>;
          })}
        </details>)}
      </div>
      <ActivityContent session={session} onSource={onSource} />
    </section>}
  </div>;
}
