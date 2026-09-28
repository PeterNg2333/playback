import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export function SourceCitation({ groups, labels, onSource, onPlay }: {
  groups: { id: string; transcriptIds: string[] }[]; labels: Map<string, string>;
  onSource?: (id: string) => void; onPlay?: (ids: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: 12, top: 12 });
  const label = groups[0].id + (groups.length > 1 ? ` +${groups.length - 1}` : "");
  useLayoutEffect(() => {
    if (!open || !anchor.current || !panel.current) return;
    const bounds = anchor.current.getBoundingClientRect(), size = panel.current.getBoundingClientRect();
    setPosition({ left: Math.max(12, Math.min(bounds.left, innerWidth - size.width - 12)),
      top: Math.max(12, Math.min(bounds.bottom + 8, innerHeight - size.height - 12)) });
  }, [open]);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (!panel.current?.contains(event.target as Node) && !anchor.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { setOpen(false); anchor.current?.focus(); } };
    document.addEventListener("pointerdown", outside); document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [open]);
  return <>
    <button ref={anchor} type="button" className="note-ref" aria-label={`Open audio sources ${label}`} aria-expanded={open}
      onClick={() => setOpen(!open)}>{label}</button>
    {open && createPortal(<div ref={panel} className="source-citation-panel" role="dialog" aria-label="Grouped audio sources" style={position}>
      <div className="term-explanation-head"><strong>Audio sources</strong><button aria-label="Close sources" onClick={() => setOpen(false)}>×</button></div>
      {groups.map(group => <section key={group.id}><strong>{group.id}</strong>
        {onPlay && <button type="button" onClick={() => onPlay(group.transcriptIds)}>Play combined passage</button>}
        <div>{group.transcriptIds.filter(id => labels.has(id)).map(id => <button type="button" key={id}
          disabled={!onSource} onClick={() => { onSource?.(id); setOpen(false); }}>Jump to {labels.get(id)}</button>)}</div>
      </section>)}
    </div>, document.body)}
  </>;
}
