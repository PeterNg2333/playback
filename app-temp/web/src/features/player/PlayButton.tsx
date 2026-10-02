import type { Chunk } from "../../lib/backend/schemas";
import { Icon } from "../../components/Icon";

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
      className="grid size-8 place-items-center rounded-[7px] border border-line bg-white text-accent hover:bg-accent-soft aria-pressed:bg-accent-soft xs:size-8.5"
      data-chunk-id={id}
      aria-label={`${playing ? "Pause" : "Play"} audio from ${startTime} to ${endTime}`}
      aria-pressed={playing}
      title={playing ? "Pause" : "Play"}
      onClick={() => onToggle(id, chunks)}
    >
      <Icon name={playing ? "pause" : "play"} className="size-3.75" />
    </button>
  );
}
