import type { Evidence, Session, TermInsight } from "../../lib/backend/schemas";
import { SourceLinks } from "../sources/SourceLinks";
import { formatDateTime } from "../../lib/time";
import { ActivityEntry, ActivityHelp } from "../activity/ActivityEntry";
import { EmptyState } from "../../components/EmptyState";

type TermDecisionTraceProps = {
  session: Session;
  onSource: (source: Evidence) => void;
};

const scoreLabelStyle = "text-[10px] text-muted";
const scoreStyle = "my-0.75 text-[13px] font-bold";

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
    return <EmptyState>No saved Jev decisions yet.</EmptyState>;
  return decisions.map((decision) => (
    <ActivityEntry
      key={decision.id}
      title={decision.term}
      detail={formatDateTime(decision.rankedAt)}
      aside={decision.highlight ? "Highlighted" : "Not highlighted"}
      asideClassName={decision.highlight ? "text-accent" : undefined}
    >
      {() => (
        <div className="px-3 pb-3">
          <dl className="my-2.5 flex flex-wrap gap-3">
            <div>
              <dt className={scoreLabelStyle}>Explain probability</dt>
              <dd
                className={scoreStyle}
                title={decision.jevProbability?.toString()}
              >
                {percentage(decision.jevProbability)}
              </dd>
            </div>
            <div>
              <dt className={scoreLabelStyle}>Category</dt>
              <dd className={scoreStyle}>
                {decision.jevRank || "Not recorded"}
              </dd>
            </div>
            <div>
              <dt className={scoreLabelStyle}>Category confidence</dt>
              <dd
                className={scoreStyle}
                title={decision.jevConfidence?.toString()}
              >
                {percentage(decision.jevConfidence)}
              </dd>
            </div>
          </dl>
          <ActivityHelp>
            {decision.decisionRule ||
              "The rule was not recorded for this decision."}
          </ActivityHelp>
          {decision.jevModel && (
            <ActivityHelp>{modelLabel(decision)}</ActivityHelp>
          )}
          <SourceLinks
            session={session}
            transcriptIds={decision.transcriptIds}
            materialIds={decision.materialIds}
            onSelect={onSource}
          />
        </div>
      )}
    </ActivityEntry>
  ));
}
