import { recordedRange } from "../../../lib/time";
import type { Chunk } from "../../../lib/backend/schemas";
import type { AudioPlayer } from "../../player/useAudioPlayer";
import { PlayButton } from "../../player/PlayButton";
import {
  AudioSourceBadge,
  audioSourceLabel,
} from "../../recording/AudioSourceBadge";
import { chunkStatus } from "../asrStatus";

// Saved audio that has no transcript yet: its ASR status, playback, and a manual retry once
// automatic retries stopped. Neighbouring parts with the same status share one row.
export function AudioRow({
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
  const first = chunks[0];
  const last = chunks.at(-1)!;
  const range = {
    start: recordedRange(
      first.recordedAt,
      sessionCreatedAt,
      first.startMs,
      first.endMs,
    ).start,
    end: recordedRange(
      last.recordedAt,
      sessionCreatedAt,
      last.startMs,
      last.endMs,
    ).end,
  };
  const stoppedIds = chunks
    .filter((chunk) => chunk.status === "asr-manual")
    .map((chunk) => chunk.id);
  const label = stoppedIds.length
    ? "ASR stopped · audio saved"
    : chunkStatus(first.status);
  return (
    <article
      className={`record-row transcript-row audio-row ${stoppedIds.length ? "asr-manual-row" : ""}`}
      id={first.id}
      data-testid="audio-row"
    >
      <time className="record-time">{range.start}</time>
      <div className="record-main">
        <div className="record-meta">
          <AudioSourceBadge sourceId={first.sourceId} />
          <span>
            {range.start}–{range.end}
          </span>
        </div>
        <div className="record-line">
          <details className="record-copy">
            <summary className="record-summary">{label}</summary>
            <div className="record-extra">
              <span>
                {range.start}–{range.end}
              </span>
              <span className="speaker">
                {audioSourceLabel(first.sourceId)}
              </span>
              {chunks.length > 1 && <p>{chunks.length} audio parts grouped</p>}
              {first.error && (
                <p className="capture-error" role="alert">
                  {first.error}
                </p>
              )}
            </div>
          </details>
          <PlayButton
            id={first.id}
            chunks={chunks}
            startTime={range.start}
            endTime={range.end}
            playingKey={playingKey}
            onToggle={onTogglePlayback}
          />
        </div>
        {stoppedIds.length > 0 && (
          <button
            className="retry-asr"
            disabled={retryDisabled}
            onClick={() => onRetry(stoppedIds)}
          >
            Retry ASR for {stoppedIds.length} saved audio part
            {stoppedIds.length === 1 ? "" : "s"}
          </button>
        )}
      </div>
    </article>
  );
}
