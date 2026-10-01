import type { Evidence, Session } from "../../lib/backend/schemas";
import { recordedRange } from "../../lib/time";
import { Chip } from "../../components/Chip";

// A source the session no longer has, named by the start of its ID.
const missingStyle = "text-[11px] font-semibold";

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
    <div className="my-1.25 flex flex-wrap gap-1.25 text-[10px] text-muted">
      {transcriptIds.map((id) => {
        const transcript = session.transcripts.find((item) => item.id === id);
        if (!transcript)
          return (
            <span className={missingStyle} key={id} title={id}>
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
          <Chip
            type="button"
            key={id}
            title={id}
            onClick={() => onSelect({ kind: "lecture", id })}
          >
            {range.start}
          </Chip>
        );
      })}
      {materialIds.map((id) => {
        const material = session.materials.find((item) => item.id === id);
        if (!material)
          return (
            <span className={missingStyle} key={id} title={id}>
              Material {id.slice(0, 8)}
            </span>
          );
        return (
          <Chip
            type="button"
            key={id}
            onClick={() => onSelect({ kind: "material", id })}
          >
            {material.name}
          </Chip>
        );
      })}
    </div>
  );
}
