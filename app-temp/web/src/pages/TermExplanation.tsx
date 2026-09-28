import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { TermInsight } from "../types/api";
import { Markdown } from "../Component/Markdown";

export function TermExplanation({
  insight,
  onClose,
  anchor,
  onMouseEnter,
  onMouseLeave,
  onAsk,
}: {
  sessionId: string;
  insight: TermInsight;
  onClose: () => void;
  anchor?: HTMLElement | null;
  onMouseEnter?: () => void;
  onMouseLeave?: () => void;
  onAsk?: () => void;
}) {
  const panel = useRef<HTMLElement>(null);
  const [position, setPosition] = useState<{ left: number; top: number }>();
  useLayoutEffect(() => {
    if (!anchor || !panel.current) return;
    const bounds = anchor.getBoundingClientRect();
    const size = panel.current.getBoundingClientRect();
    setPosition({ left: Math.max(12, Math.min(bounds.left, innerWidth - size.width - 12)),
      top: Math.max(12, Math.min(bounds.bottom + 8, innerHeight - size.height - 12)) });
  }, [anchor, insight.explanation]);
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);
  useEffect(() => {
    if (!anchor) return;
    const closeOnScroll = (event: Event) => {
      if (event.target instanceof Node && panel.current?.contains(event.target)) return;
      onClose();
    };
    window.addEventListener("scroll", closeOnScroll, true);
    window.addEventListener("resize", onClose);
    return () => { window.removeEventListener("scroll", closeOnScroll, true); window.removeEventListener("resize", onClose); };
  }, [anchor, onClose]);
  return createPortal(
    <aside
      ref={panel}
      className="term-explanation"
      role="dialog"
      aria-label={`${insight.term} explanation`}
      style={position ? { ...position, right: "auto", bottom: "auto" } : undefined}
      onMouseEnter={onMouseEnter} onMouseLeave={onMouseLeave} onFocus={onMouseEnter} onBlur={onMouseLeave}
    >
      <div className="term-explanation-head">
        <strong>{insight.term}</strong>
        <button type="button" aria-label="Close explanation" onClick={onClose}>
          ×
        </button>
      </div>
      {insight.explanation ? (
        <Markdown value={insight.explanation} />
      ) : (
        <p>Explanation is pending automatic processing. Check AI activity for progress or errors.</p>
      )}
      {insight.evidence.length > 0 && (
        <div className="term-explanation-sources">
          {insight.evidence
            .filter((source) => source.url.startsWith("https://"))
            .map((source) => (
              <a
                key={source.url}
                href={source.url}
                target="_blank"
                rel="noopener noreferrer"
              >
                {source.title}
              </a>
            ))}
        </div>
      )}
      <small>AI/web supplement · separate from lecture evidence</small>
      {onAsk && <button className="term-followup" type="button" onClick={onAsk}>Ask a follow-up in chat</button>}
    </aside>, document.body
  );
}
