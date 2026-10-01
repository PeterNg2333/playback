import { recordedRange } from "../../../lib/time";
import type { CaptureStatus } from "../../../types/api";
import { AudioSourceBadge } from "../../recording/AudioSourceBadge";

type ActiveSegment = NonNullable<CaptureStatus["activeSegments"]>[number];

// Speech the recorder is capturing now, shown faded until its audio is saved; with interim text once ASR sends some.
export function LiveRow({
  segment,
  sessionCreatedAt,
}: {
  segment: ActiveSegment;
  sessionCreatedAt?: string;
}) {
  const range = recordedRange(
    segment.recordedAt,
    sessionCreatedAt,
    segment.startMs,
    segment.endMs,
  );
  return (
    <article
      className="record-row transcript-row audio-row live-segment"
      data-streaming={!!segment.streaming}
      role="status"
      aria-label={`${segment.sourceId} audio recording in progress`}
    >
      <span className="record-time">{range.start}</span>
      <div className="record-main">
        <div className="record-meta">
          <AudioSourceBadge sourceId={segment.sourceId} />
          <span>Started {range.start}</span>
        </div>
        <div className="record-line">
          <span className="record-summary">
            <span className="live-dot" />{" "}
            {segment.interimText
              ? "Interim transcription"
              : segment.streaming
                ? "Speech active · recording audio"
                : "Speech detected · recording audio"}
          </span>
          <span className="live-pending">
            {segment.interimText ? "Awaiting final…" : "Saving…"}
          </span>
        </div>
        {segment.interimText && (
          <p className="interim-text">{segment.interimText}</p>
        )}
      </div>
    </article>
  );
}
