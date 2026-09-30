import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { TermInsight } from "../types/api";
import { Markdown } from "../Component/Markdown";
import { api } from "./api";
import { TermInsightSchema } from "../types/api";

export function TermExplanation({
  insight,
  onClose,
  anchor,
  onMouseEnter,
  onMouseLeave,
  onAsk,
  sessionId,
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
  const [expanded, setExpanded] = useState(false);
  const [detailed, setDetailed] = useState<TermInsight>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const request = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => request.current?.abort(), []);
  const shown = detailed ?? insight;
  useLayoutEffect(() => {
    if (!anchor || !panel.current) return;
    const bounds = anchor.getBoundingClientRect();
    const size = panel.current.getBoundingClientRect();
    setPosition({ left: Math.max(12, Math.min(bounds.left, innerWidth - size.width - 12)),
      top: Math.max(12, Math.min(bounds.bottom + 8, innerHeight - size.height - 12)) });
  }, [anchor, insight.explanation, expanded]);
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
        <><Markdown value={expanded ? shown.explanation ?? "" : shown.explanationSummary ?? shown.explanation ?? ""} />
        {shown.explanationSummary && shown.explanationSummary !== shown.explanation &&
          <button className="term-followup" onClick={() => setExpanded(x => !x)} aria-expanded={expanded}>{expanded ? "Show short explanation" : "Read full explanation"}</button>}
        {shown.explanationVersion !== "term-detail-v2" && <button className="term-followup" disabled={pending} onClick={async () => {
          const controller = new AbortController(); request.current?.abort(); request.current = controller; setPending(true); setError("");
          try { const result = await api(`/sessions/${sessionId}/terms/${insight.id}/explain?detail=true`, "POST", undefined, TermInsightSchema, controller.signal);
            if (!controller.signal.aborted) { setDetailed(result); setExpanded(true); }
          } catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : String(e)); }
          finally { if (!controller.signal.aborted) setPending(false); }
        }}>{pending ? "Generating…" : "Generate saved detailed explanation"}</button>}
        {error && <p role="alert">{error}</p>}
        </>
      ) : (
        <p>Explanation is pending automatic processing. Check AI activity for progress or errors.</p>
      )}
      {shown.evidence.length > 0 && (
        <div className="term-explanation-sources">
          {shown.evidence
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
