import type { Answer, Evidence, Session } from "../../lib/backend/schemas";
import { Markdown } from "../../components/markdown/Markdown";
import { CitedMarkdown } from "../sources/CitedMarkdown";
import { Chip } from "../../components/Chip";
import { ChatError } from "./ChatControls";

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
      className="rounded-[10px] bg-accent-soft p-3 text-[12px] [&_p]:mb-2.25 [&_p:last-child]:mb-0"
      data-testid="answer"
    >
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
      {answer.lectureError && (
        <ChatError>Lecture evidence: {answer.lectureError}</ChatError>
      )}
      {answer.webError && (
        <ChatError>Public web search failed: {answer.webError}</ChatError>
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
      {answer.inference && (
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
