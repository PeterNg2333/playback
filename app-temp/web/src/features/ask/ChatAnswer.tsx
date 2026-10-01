import type { Answer, Evidence, Session } from "../../lib/backend/schemas";
import { Markdown } from "../../Component/markdown/Markdown";
import { CitedMarkdown } from "../sources/CitedMarkdown";

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
    <div className="answer">
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
        <p className="chat-error" role="alert">
          Lecture evidence: {answer.lectureError}
        </p>
      )}
      {answer.webError && (
        <p className="chat-error" role="alert">
          Public web search failed: {answer.webError}
        </p>
      )}
      {answer.webAnswer && (
        <section>
          <strong>Public web:</strong>
          <Markdown value={answer.webAnswer} />
        </section>
      )}
      {answer.webSuggestions && (
        <iframe
          className="search-suggestions"
          title="Google Search suggestions"
          sandbox="allow-popups allow-popups-to-escape-sandbox"
          srcDoc={answer.webSuggestions}
        />
      )}
      {answer.inference && (
        <small>Model inference · verify against sources</small>
      )}
      {!!answer.evidence.length && (
        <details className="answer-sources">
          <summary>Sources · {answer.evidence.length}</summary>
          <div>
            {answer.evidence.map((ev, index) => (
              <button className="citation" key={index} onClick={() => jump(ev)}>
                {ev.title || ev.label || ev.kind}
              </button>
            ))}
          </div>
        </details>
      )}
    </div>
  );
}
