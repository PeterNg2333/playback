import { useEffect, useRef, useState } from "react";
import type { TermInsight } from "../../types/api";
import { TermExplanation } from "./TermExplanation";

export function TermHighlight({
  sessionId,
  insight,
  text,
  onAsk,
}: {
  sessionId: string;
  insight: TermInsight;
  text: string;
  onAsk: () => void;
}) {
  const anchor = useRef<HTMLButtonElement>(null);
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [open, setOpen] = useState(false);
  const cancelClose = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    closeTimer.current = null;
  };
  const show = () => {
    cancelClose();
    setOpen(true);
  };
  const scheduleClose = () => {
    cancelClose();
    closeTimer.current = setTimeout(() => setOpen(false), 180);
  };
  useEffect(() => () => cancelClose(), []);
  return (
    <span className="term-wrap">
      <button
        ref={anchor}
        type="button"
        className="term-highlight"
        aria-label={`Explain ${text}`}
        aria-expanded={open}
        onMouseEnter={show}
        onMouseLeave={scheduleClose}
        onFocus={show}
        onBlur={scheduleClose}
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          show();
        }}
      >
        {text}
      </button>
      {open && (
        <TermExplanation
          sessionId={sessionId}
          insight={insight}
          anchor={anchor.current}
          onMouseEnter={cancelClose}
          onMouseLeave={scheduleClose}
          onClose={() => {
            cancelClose();
            setOpen(false);
          }}
          onAsk={() => {
            cancelClose();
            setOpen(false);
            onAsk();
          }}
        />
      )}
    </span>
  );
}
