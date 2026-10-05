import clsx from "clsx";
import { recordedRange } from "../../../lib/time";
import type { Chunk } from "../../../lib/backend/schemas";
import type { AudioPlayer } from "../../player/useAudioPlayer";
import { PlayButton } from "../../player/PlayButton";
import {
  AudioSourceBadge,
  audioSourceLabel,
} from "../../recording/AudioSourceBadge";
import { chunkStatus } from "../asrStatus";
import {
  RetryButton,
  RowDetails,
  RowLine,
  RowMeta,
  TimelineRow,
} from "./TimelineRow";

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
    // Audio whose recognition stopped is marked with an orange edge.
    <TimelineRow
      time={range.start}
      className={clsx(
        stoppedIds.length && "border-l-3 border-l-[#d38a4c] pl-2",
      )}
      id={first.id}
      data-testid="audio-row"
    >
      <RowMeta>
        <AudioSourceBadge sourceId={first.sourceId} />
        <span>
          {range.start}–{range.end}
        </span>
      </RowMeta>
      <RowLine>
        <RowDetails muted summary={label}>
          <span>
            {range.start}–{range.end}
          </span>
          <span className="mb-0.75 block text-[11px] font-bold text-muted">
            {audioSourceLabel(first.sourceId)}
          </span>
          {chunks.length > 1 && (
            <p className="my-0.75">{chunks.length} audio parts grouped</p>
          )}
          {first.error && (
            <p className="my-1 text-[12px] text-[#a32828]" role="alert">
              {first.error}
            </p>
          )}
        </RowDetails>
        <PlayButton
          id={first.id}
          chunks={chunks}
          startTime={range.start}
          endTime={range.end}
          playingKey={playingKey}
          onToggle={onTogglePlayback}
        />
      </RowLine>
      {stoppedIds.length > 0 && (
        <RetryButton
          className="mt-1.25 mb-0.5"
          disabled={retryDisabled}
          onClick={() => onRetry(stoppedIds)}
        >
          Retry ASR for {stoppedIds.length} saved audio part
          {stoppedIds.length === 1 ? "" : "s"}
        </RetryButton>
      )}
    </TimelineRow>
  );
}
