import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";

export type VirtualItem = { key: string; sourceIds: string[]; render: () => ReactNode; estimate?: number };
function Measured({ item, measure }: { item: VirtualItem; measure: (key: string, height: number) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const element = ref.current; if (!element) return;
    const observer = new ResizeObserver(() => measure(item.key, element.getBoundingClientRect().height));
    observer.observe(element); return () => observer.disconnect();
  }, [item.key, measure]);
  return <div ref={ref} data-virtual-row={item.key}>{item.render()}</div>;
}
export function VirtualTranscript({ items, sessionId, onReveal }: { items: VirtualItem[]; sessionId: string; onReveal?: (id: string) => void }) {
  const list = useRef<HTMLDivElement>(null);
  const heights = useRef(new Map<string, number>());
  const [revision, setRevision] = useState(0);
  const [viewport, setViewport] = useState({ top: 0, height: 800 });
  const [target, setTarget] = useState<string>();
  const revealed = useRef<{ id: string; element: HTMLElement; timer: ReturnType<typeof setTimeout> } | undefined>(undefined);
  useEffect(() => () => { if (revealed.current) { clearTimeout(revealed.current.timer); revealed.current.element.classList.remove("source-revealed"); } }, []);
  const anchor = useRef<{ key?: string; offset: number }>({ offset: 0 });
  const keys = useMemo(() => new Set(items.map(x => x.key)), [items]);
  useEffect(() => { for (const key of heights.current.keys()) if (!keys.has(key)) heights.current.delete(key); }, [keys]);
  const offsets = useMemo(() => {
    const result = [0]; for (const item of items) result.push(result.at(-1)! + (heights.current.get(item.key) ?? item.estimate ?? 110));
    return result;
  }, [items, revision]);
  const find = (position: number) => {
    let left = 0, right = items.length;
    while (left < right) { const middle = (left + right) >>> 1; if (offsets[middle + 1] < position) left = middle + 1; else right = middle; }
    return left;
  };
  const start = Math.max(0, find(Math.max(0, viewport.top - 450)));
  const end = Math.min(items.length, find(viewport.top + viewport.height + 450) + 1);
  const measure = useMemo(() => (key: string, height: number) => {
    if (Math.abs((heights.current.get(key) ?? -1) - height) < 1) return;
    heights.current.set(key, height); setRevision(x => x + 1);
  }, []);
  useLayoutEffect(() => {
    const parent = list.current?.closest<HTMLElement>(".transcript-content"); if (!parent || !list.current) return;
    let frame = 0;
    const update = () => {
      cancelAnimationFrame(frame); frame = requestAnimationFrame(() => {
        const top = parent.getBoundingClientRect().top - list.current!.getBoundingClientRect().top;
        setViewport(old => old.top === top && old.height === parent.clientHeight ? old : { top, height: parent.clientHeight });
      });
    };
    parent.addEventListener("scroll", update, { passive: true }); const observer = new ResizeObserver(update); observer.observe(parent);
    update(); return () => { parent.removeEventListener("scroll", update); observer.disconnect(); cancelAnimationFrame(frame); };
  }, [sessionId]);
  useLayoutEffect(() => {
    const parent = list.current?.closest<HTMLElement>(".transcript-content"); if (!parent || !list.current) return;
    const previous = anchor.current;
    const index = previous.key ? items.findIndex(x => x.key === previous.key) : -1;
    const actualTop = parent.getBoundingClientRect().top - list.current.getBoundingClientRect().top;
    // A wheel/seek can arrive before the rAF viewport update. Do not restore the
    // previous anchor over that user scroll when measurements/interim text change.
    if (index >= 0 && !target && Math.abs(actualTop - viewport.top) < 1) parent.scrollTop += offsets[index] + previous.offset - actualTop;
    const top = parent.getBoundingClientRect().top - list.current.getBoundingClientRect().top;
    const visible = find(top);
    anchor.current = { key: items[visible]?.key, offset: top - offsets[visible] };
  }, [offsets]);
  useLayoutEffect(() => {
    const visible = find(viewport.top);
    anchor.current = { key: items[visible]?.key, offset: viewport.top - offsets[visible] };
  }, [viewport.top]);
  useEffect(() => {
    const reveal = (event: Event) => {
      const detail = (event as CustomEvent<{ sessionId: string; id: string }>).detail;
      if (detail.sessionId !== sessionId) return;
      onReveal?.(detail.id); setTarget(detail.id);
    };
    window.addEventListener("playback-reveal-source", reveal); return () => window.removeEventListener("playback-reveal-source", reveal);
  }, [sessionId, onReveal]);
  useLayoutEffect(() => {
    if (!target) return;
    const index = items.findIndex(x => x.sourceIds.includes(target));
    const parent = list.current?.closest<HTMLElement>(".transcript-content");
    if (index >= 0 && (index < start || index >= end) && parent && list.current) {
      parent.scrollTop += list.current.getBoundingClientRect().top - parent.getBoundingClientRect().top + offsets[index];
      setViewport({ top: offsets[index], height: parent.clientHeight }); return;
    }
    const element = document.getElementById(target);
    if (!element) return;
    if (revealed.current?.id === target && revealed.current.element === element) return;
    if (revealed.current) { clearTimeout(revealed.current.timer); revealed.current.element.classList.remove("source-revealed"); }
    for (let details = element.closest("details"); details; details = details.parentElement?.closest("details") ?? null) details.open = true;
    element.querySelector("details")?.setAttribute("open", "");
    element.classList.add("source-revealed"); element.tabIndex = -1; element.focus({ preventScroll: true });
    element.scrollIntoView({ block: "center" });
    const timer = setTimeout(() => { element.classList.remove("source-revealed"); revealed.current = undefined; setTarget(undefined); }, 1600);
    revealed.current = { id: target, element, timer };
  }, [target, start, end, items, revision]);
  return <div className="virtual-transcript" ref={list} data-total-rows={items.length}>
    <div aria-hidden="true" style={{ height: offsets[start] }} />
    {items.slice(start, end).map(item => <Measured key={item.key} item={item} measure={measure} />)}
    <div aria-hidden="true" style={{ height: offsets.at(-1)! - offsets[end] }} />
  </div>;
}
