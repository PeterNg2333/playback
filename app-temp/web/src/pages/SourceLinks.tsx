import type { Evidence, Session } from "../types/api";
import { recordedRange } from "../lib/time";

type SourceLinksProps = {
  session: Session;
  transcriptIds: string[];
  materialIds: string[];
  onSelect: (source: Evidence) => void;
};

export function SourceLinks({
  session,
  transcriptIds,
  materialIds,
  onSelect,
}: SourceLinksProps) {
  return (
    <div className="activity-sources">
      {transcriptIds.map((id) => {
        const transcript = session.transcripts.find((item) => item.id === id);
        if (!transcript)
          return (
            <span key={id} title={id}>
              Audio {id.slice(0, 8)}
            </span>
          );
        const range = recordedRange(
          transcript.recordedAt,
          session.createdAt,
          transcript.startMs,
          transcript.endMs,
        );
        return (
          <button
            type="button"
            className="citation"
            key={id}
            title={id}
            onClick={() => onSelect({ kind: "lecture", id })}
          >
            {range.start}
          </button>
        );
      })}
      {materialIds.map((id) => {
        const material = session.materials.find((item) => item.id === id);
        if (!material)
          return (
            <span key={id} title={id}>
              Material {id.slice(0, 8)}
            </span>
          );
        return (
          <button
            type="button"
            className="citation"
            key={id}
            onClick={() => onSelect({ kind: "material", id })}
          >
            {material.name}
          </button>
        );
      })}
    </div>
  );
}
