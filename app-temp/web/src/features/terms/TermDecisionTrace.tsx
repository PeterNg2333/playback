import type { Evidence, Session, TermInsight } from "../../lib/backend/schemas";
import { SourceLinks } from "../sources/SourceLinks";
import { formatDateTime } from "../../lib/time";
import { LazyDetails } from "../../components/LazyDetails";

type TermDecisionTraceProps = {
  session: Session;
  onSource: (source: Evidence) => void;
};

function percentage(value?: number) {
  return value === undefined
    ? "Not recorded"
    : `${Number((value * 100).toFixed(2))}%`;
}

function modelLabel(decision: TermInsight) {
  if (!decision.jevModel) return null;
  let label = `Model: ${decision.jevModel}`;
  if (decision.jevCached === true) label += " · Cached result";
  if (decision.jevCached === false) label += " · Provider result";
  return label;
}

export function TermDecisionTrace({
  session,
  onSource,
}: TermDecisionTraceProps) {
  const decisions = [...(session.termInsights || [])].sort((a, b) =>
    (b.rankedAt || "").localeCompare(a.rankedAt || ""),
  );
  if (!decisions.length)
    return <p className="empty">No saved Jev decisions yet.</p>;
  return decisions.map((decision) => (
    <LazyDetails
      className="activity-entry"
      key={decision.id}
      summary={
        <>
          <strong>{decision.term}</strong>
          <span>{formatDateTime(decision.rankedAt)}</span>
          <small
            className="decision-outcome"
            data-highlight={decision.highlight}
          >
            {decision.highlight ? "Highlighted" : "Not highlighted"}
          </small>
        </>
      }
    >
      {() => (
        <div className="activity-entry-body">
          <dl className="decision-scores">
            <div>
              <dt>Explain probability</dt>
              <dd title={decision.jevProbability?.toString()}>
                {percentage(decision.jevProbability)}
              </dd>
            </div>
            <div>
              <dt>Category</dt>
              <dd>{decision.jevRank || "Not recorded"}</dd>
            </div>
            <div>
              <dt>Category confidence</dt>
              <dd title={decision.jevConfidence?.toString()}>
                {percentage(decision.jevConfidence)}
              </dd>
            </div>
          </dl>
          <p className="activity-help">
            {decision.decisionRule ||
              "The rule was not recorded for this decision."}
          </p>
          {decision.jevModel && (
            <p className="activity-help">{modelLabel(decision)}</p>
          )}
          <SourceLinks
            session={session}
            transcriptIds={decision.transcriptIds}
            materialIds={decision.materialIds}
            onSelect={onSource}
          />
        </div>
      )}
    </LazyDetails>
  ));
}
