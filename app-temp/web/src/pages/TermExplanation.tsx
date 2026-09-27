import { useEffect, useState } from "react";
import type { TermInsight } from "../types/api";
import { TermInsightSchema } from "../types/api";
import { api } from "./api";

export function TermExplanation({
  sessionId,
  insight,
  onClose,
}: {
  sessionId: string;
  insight: TermInsight;
  onClose: () => void;
}) {
  const [current, setCurrent] = useState(insight);
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    if (!insight.explanation) {
      api(
        `/sessions/${sessionId}/terms/${insight.id}/explain`,
        "POST",
        undefined,
        TermInsightSchema,
      )
        .then((value) => {
          if (active) setCurrent(value);
        })
        .catch((reason) => {
          if (active)
            setError(reason instanceof Error ? reason.message : String(reason));
        });
    }
    return () => {
      active = false;
    };
  }, [sessionId, insight.id]);
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);
  return (
    <aside
      className="term-explanation"
      role="dialog"
      aria-label={`${insight.term} explanation`}
    >
      <div className="term-explanation-head">
        <strong>{insight.term}</strong>
        <button type="button" aria-label="Close explanation" onClick={onClose}>
          ×
        </button>
      </div>
      {error ? (
        <p role="alert">{error}</p>
      ) : current.explanation ? (
        <p>{current.explanation}</p>
      ) : (
        <p>Loading explanation…</p>
      )}
      {current.evidence.length > 0 && (
        <div className="term-explanation-sources">
          {current.evidence
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
      <small>Saved separately from lecture notes</small>
    </aside>
  );
}
