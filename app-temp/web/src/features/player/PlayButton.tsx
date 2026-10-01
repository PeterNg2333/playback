import type { Chunk } from "../../lib/backend/schemas";
import { Icon } from "../../Component/Icon";

export function PlayButton({
  id,
  chunks,
  startTime,
  endTime,
  playingKey,
  onToggle,
}: {
  id: string;
  chunks: Pick<Chunk, "id" | "startMs" | "endMs" | "recordedAt">[];
  startTime: string;
  endTime: string;
  playingKey: string | null;
  onToggle: (
    key: string,
    chunks: Pick<Chunk, "id" | "startMs" | "endMs" | "recordedAt">[],
  ) => void;
}) {
  const playing = playingKey === id;
  return (
    <button
      className="record-play"
      data-chunk-id={id}
      aria-label={`${playing ? "Pause" : "Play"} audio from ${startTime} to ${endTime}`}
      aria-pressed={playing}
      title={playing ? "Pause" : "Play"}
      onClick={() => onToggle(id, chunks)}
    >
      <Icon name={playing ? "pause" : "play"} />
    </button>
  );
}
