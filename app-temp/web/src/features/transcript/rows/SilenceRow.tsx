import { recordedRange } from "../../../lib/time";
import type { Chunk } from "../../../lib/backend/schemas";
import type { AudioPlayer } from "../../player/useAudioPlayer";
import { PlayButton } from "../../player/PlayButton";
import {
  AudioSourceBadge,
  audioSourceLabel,
} from "../../recording/AudioSourceBadge";

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
    <details className="quiet-section">
      <summary>
        <span className="quiet-rule" />
        <span>No audio · {chunks.length} parts</span>
        <span className="quiet-rule" />
      </summary>
      <div className="quiet-section-body">
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
          <button
            className="retry-asr"
            disabled={retryDisabled}
            onClick={() => onRetry(emptyIds)}
          >
            Retry ASR
          </button>
        )}
        <div className="quiet-parts">
          {chunks.map((chunk) => {
            const part = recordedRange(
              chunk.recordedAt,
              sessionCreatedAt,
              chunk.startMs,
              chunk.endMs,
            );
            return (
              <div className="quiet-part" id={chunk.id} key={chunk.id}>
                <AudioSourceBadge sourceId={chunk.sourceId} />
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
