import { useMutation } from "@tanstack/react-query";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { TermInsight } from "../../lib/backend/schemas";
import { Markdown } from "../../components/markdown/Markdown";
import { api } from "../../lib/backend/client";
import { TermInsightSchema } from "../../lib/backend/schemas";
import { PopupHeader } from "../../components/PopupHeader";

const followUpStyle =
  "mt-3 block cursor-pointer rounded-[7px] border border-line bg-accent-soft px-2.5 py-1.75 text-[12px] text-accent";

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
  // Generates and saves a longer explanation for this term once, on request.
  const detail = useMutation({
    mutationFn: () =>
      api.post(
        `/sessions/${sessionId}/terms/${insight.id}/explain?detail=true`,
        undefined,
        TermInsightSchema,
      ),
  });
  const shown = detail.data ?? insight;
  useLayoutEffect(() => {
    if (!anchor || !panel.current) return;
    const bounds = anchor.getBoundingClientRect();
    const size = panel.current.getBoundingClientRect();
    setPosition({
      left: Math.max(12, Math.min(bounds.left, innerWidth - size.width - 12)),
      top: Math.max(
        12,
        Math.min(bounds.bottom + 8, innerHeight - size.height - 12),
      ),
    });
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
      if (event.target instanceof Node && panel.current?.contains(event.target))
        return;
      onClose();
    };
    window.addEventListener("scroll", closeOnScroll, true);
    window.addEventListener("resize", onClose);
    return () => {
      window.removeEventListener("scroll", closeOnScroll, true);
      window.removeEventListener("resize", onClose);
    };
  }, [anchor, onClose]);
  // The panel spaces its own paragraphs, including the explanation's Markdown.
  return createPortal(
    <aside
      ref={panel}
      className="fixed right-4.5 bottom-44.5 z-30 max-h-[min(380px,65vh)] w-[min(320px,calc(100vw-36px))] overflow-auto rounded-xl border border-line bg-white p-3.75 shadow-[0_18px_48px_#25243c33] md:bottom-41.25 [&_p]:my-2.5 [&_p]:text-[12px] [&_p]:leading-[1.6] [&_p]:whitespace-pre-wrap"
      role="dialog"
      aria-label={`${insight.term} explanation`}
      style={
        position ? { ...position, right: "auto", bottom: "auto" } : undefined
      }
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      onFocus={onMouseEnter}
      onBlur={onMouseLeave}
    >
      <PopupHeader
        title={insight.term}
        closeLabel="Close explanation"
        onClose={onClose}
      />
      {insight.explanation ? (
        <>
          <Markdown
            value={
              expanded
                ? (shown.explanation ?? "")
                : (shown.explanationSummary ?? shown.explanation ?? "")
            }
          />
          {shown.explanationSummary &&
            shown.explanationSummary !== shown.explanation && (
              <button
                className={followUpStyle}
                onClick={() => setExpanded((x) => !x)}
                aria-expanded={expanded}
              >
                {expanded ? "Show short explanation" : "Read full explanation"}
              </button>
            )}
          {shown.explanationVersion !== "term-detail-v2" && (
            <button
              className={followUpStyle}
              disabled={detail.isPending}
              onClick={() =>
                detail.mutate(undefined, { onSuccess: () => setExpanded(true) })
              }
            >
              {detail.isPending
                ? "Generating…"
                : "Generate saved detailed explanation"}
            </button>
          )}
          {detail.error && <p role="alert">{detail.error.message}</p>}
        </>
      ) : (
        <p>
          Explanation is pending automatic processing. Check AI activity for
          progress or errors.
        </p>
      )}
      {shown.evidence.length > 0 && (
        <div className="mb-2 flex flex-wrap gap-1.5">
          {shown.evidence
            .filter((source) => source.url.startsWith("https://"))
            .map((source) => (
              <a
                className="text-[11px] [color:revert] underline"
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
      <small className="text-[10px] text-muted">
        AI/web supplement · separate from lecture evidence
      </small>
      {onAsk && (
        <button className={followUpStyle} type="button" onClick={onAsk}>
          Ask a follow-up in chat
        </button>
      )}
    </aside>,
    document.body,
  );
}
