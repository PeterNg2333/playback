import type { Chunk } from "../types/api";
import { Icon } from "../Component/Icon";
import { time } from "./format";

export function RecordPlay({
  id,
  chunks,
  startMs,
  endMs,
  playingKey,
  onToggle,
}: {
  id: string;
  chunks: Pick<Chunk, "id">[];
  startMs: number;
  endMs: number;
  playingKey: string | null;
  onToggle: (key: string, chunks: Pick<Chunk, "id">[]) => void;
}) {
  const playing = playingKey === id;
  return (
    <button
      className="record-play"
      aria-label={`${playing ? "Stop" : "Play"} audio from ${time(startMs)} to ${time(endMs)}`}
      aria-pressed={playing}
      title={playing ? "Stop" : "Play"}
      onClick={() => onToggle(id, chunks)}
    >
      <Icon name={playing ? "stop" : "play"} />
    </button>
  );
}
