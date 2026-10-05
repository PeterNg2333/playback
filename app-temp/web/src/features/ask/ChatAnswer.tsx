import type { Answer, Evidence, Session } from "../../lib/backend/schemas";
import { Markdown } from "../../components/markdown/Markdown";
import { CitedMarkdown } from "../sources/CitedMarkdown";
import { Chip } from "../../components/Chip";

export function ChatAnswer({
  answer,
  jump,
  sourceGroups,
}: {
  answer: Answer;
  jump: (evidence: Evidence) => void;
  sourceGroups?: Session["sourceGroups"];
}) {
  return (
    // Paragraphs in an answer sit closer together than in the notes.
    <div
      className="min-w-0 rounded-[10px] bg-accent-soft p-3 text-[12px] [overflow-wrap:anywhere] [&_p]:mb-2.25 [&_p:last-child]:mb-0"
      data-testid="answer"
    >
      {answer.lectureStatus === "unverified" && (
        <p className="mb-2 text-[11px] font-semibold text-amber-800">
          General answer · Not verified against sources
        </p>
      )}
      {answer.lectureStatus === "unverified" ? (
        <Markdown value={answer.answer} />
      ) : (
        <CitedMarkdown
          value={answer.answer}
          groups={sourceGroups}
          sources={answer.evidence
            .filter((ev) => !!ev.id)
            .map((ev) => ({
              id: ev.id!,
              label: ev.label || ev.title || ev.kind,
            }))}
          onSource={(id) => {
            const ev = answer.evidence.find((item) => item.id === id);
            if (ev) jump(ev);
          }}
        />
      )}
      {(answer.lectureError || answer.webError) && (
        <details className="my-2 rounded-lg border border-amber-200 bg-amber-50 p-2 text-[11px] text-amber-900">
          <summary className="cursor-pointer">
            {answer.webError
              ? "Web sources unavailable"
              : "Lecture sources could not be verified"}
          </summary>
          {answer.lectureError && (
            <p className="mt-2">Lecture: {answer.lectureError}</p>
          )}
          {answer.webError && (
            <p className="mt-2">Web search: {answer.webError}</p>
          )}
        </details>
      )}
      {answer.webAnswer && (
        <section>
          <strong>Public web:</strong>
          <Markdown value={answer.webAnswer} />
        </section>
      )}
      {answer.webSuggestions && (
        <iframe
          className="my-2 block h-18 w-full bg-white"
          title="Google Search suggestions"
          sandbox="allow-popups allow-popups-to-escape-sandbox"
          srcDoc={answer.webSuggestions}
        />
      )}
      {answer.inference && answer.lectureStatus !== "unverified" && (
        <small className="my-2.25 block text-[smaller] text-muted">
          Model inference · verify against sources
        </small>
      )}
      {!!answer.evidence.length && (
        <details className="mt-3 text-[11px]">
          <summary>Sources · {answer.evidence.length}</summary>
          <div>
            {answer.evidence.map((ev, index) => (
              <Chip onTint key={index} onClick={() => jump(ev)}>
                {ev.title || ev.label || ev.kind}
              </Chip>
            ))}
          </div>
        </details>
      )}
    </div>
  );
}
