import { recordedRange } from "../../../lib/time";
import type { Chunk } from "../../../lib/backend/schemas";
import type { AudioPlayer } from "../../player/useAudioPlayer";
import { PlayButton } from "../../player/PlayButton";
import {
  AudioSourceBadge,
  audioSourceLabel,
} from "../../recording/AudioSourceBadge";
import { RetryButton } from "./TimelineRow";

const quietRule = "flex-1 border-t border-dashed border-line";

// One hour's audio with no recognised words, folded into a single "No audio" line that can be
// expanded to play each part or send the empty ones back to ASR.
export function SilenceRow({
  chunks,
  sessionCreatedAt,
  playingKey,
  onTogglePlayback,
  retryDisabled,
  onRetry,
}: {
  chunks: Chunk[];
  sessionCreatedAt?: string;
  playingKey: string | null;
  onTogglePlayback: AudioPlayer["togglePlayback"];
  retryDisabled: boolean;
  onRetry: (chunkIds: string[]) => void;
}) {
  const emptyIds = chunks
    .filter((chunk) => chunk.status === "asr-empty")
    .map((chunk) => chunk.id);
  return (
    <details
      className="group/quiet my-1.25 text-muted"
      data-testid="silence-row"
    >
      <summary className="flex min-h-8 cursor-pointer list-none items-center gap-2.25 text-[11px] before:text-accent before:content-['▸'] group-open/quiet:before:content-['▾'] [&::-webkit-details-marker]:hidden">
        <span className={quietRule} />
        <span>No audio · {chunks.length} parts</span>
        <span className={quietRule} />
      </summary>
      <div className="grid justify-items-start gap-2.5 pt-1 pr-2.25 pb-3 pl-4.75 text-[11px]">
        <span>
          {emptyIds.length
            ? `${emptyIds.length} parts returned no words from ASR. `
            : ""}
          Saved audio from{" "}
          {Array.from(
            new Set(chunks.map((chunk) => audioSourceLabel(chunk.sourceId))),
          ).join(" + ")}
        </span>
        {emptyIds.length > 0 && (
          <RetryButton
            disabled={retryDisabled}
            onClick={() => onRetry(emptyIds)}
          >
            Retry ASR
          </RetryButton>
        )}
        <div className="grid max-h-60 w-full overflow-auto rounded-lg border border-line bg-white">
          {chunks.map((chunk) => {
            const part = recordedRange(
              chunk.recordedAt,
              sessionCreatedAt,
              chunk.startMs,
              chunk.endMs,
            );
            return (
              <div
                className="grid min-h-10.5 grid-cols-[minmax(90px,1fr)_auto_32px] items-center gap-1.25 border-b border-line px-2 py-1 tabular-nums last:border-b-0 md:grid-cols-[minmax(110px,1fr)_auto_34px] md:gap-2.5"
                id={chunk.id}
                key={chunk.id}
              >
                <AudioSourceBadge sourceId={chunk.sourceId} small />
                <span>
                  {part.start}–{part.end}
                </span>
                <PlayButton
                  id={chunk.id}
                  chunks={[chunk]}
                  startTime={part.start}
                  endTime={part.end}
                  playingKey={playingKey}
                  onToggle={onTogglePlayback}
                />
              </div>
            );
          })}
        </div>
      </div>
    </details>
  );
}
