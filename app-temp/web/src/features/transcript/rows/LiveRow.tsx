import clsx from "clsx";
import { recordedRange } from "../../../lib/time";
import type { CaptureStatus } from "../../../lib/backend/schemas";
import { AudioSourceBadge } from "../../recording/AudioSourceBadge";
import { RowLine, RowMeta, TimelineRow, rowSummaryStyle } from "./TimelineRow";

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
    <TimelineRow
      time={range.start}
      className="rounded-[7px] border-l-3 border-l-[#ce5186] bg-[#fff7fa] pl-2 opacity-58"
      mainClassName="bg-[#fff7fa]"
      data-testid="live-row"
      role="status"
      aria-label={`${segment.sourceId} audio recording in progress`}
    >
      <RowMeta>
        <AudioSourceBadge sourceId={segment.sourceId} />
        <span>Started {range.start}</span>
      </RowMeta>
      <RowLine>
        <span
          className={clsx(
            rowSummaryStyle,
            "inline-flex items-center gap-2.25 text-muted",
          )}
        >
          <LiveDot breathing={!!segment.streaming} className="flex-none" />{" "}
          {segment.interimText
            ? "Interim transcription"
            : segment.streaming
              ? "Speech active · recording audio"
              : "Speech detected · recording audio"}
        </span>
        <span className="text-[11px] font-bold text-[#943e67]">
          {segment.interimText ? "Awaiting final…" : "Saving…"}
        </span>
      </RowLine>
      {segment.interimText && (
        <p
          className="mt-2 whitespace-pre-wrap text-muted"
          data-testid="interim-text"
        >
          {segment.interimText}
        </p>
      )}
    </TimelineRow>
  );
}

// The recording dot: pink, breathing while speech streams in; grey while paused.
export function LiveDot({
  breathing,
  paused = false,
  className,
}: {
  breathing: boolean;
  paused?: boolean;
  className?: string;
}) {
  return (
    <span
      className={clsx(
        "size-2.25 rounded-full",
        paused ? "bg-muted" : "bg-[#ce5186] shadow-[0_0_0_4px_#f8dbe8]",
        breathing && !paused && "animate-record-breathe",
        className,
      )}
    />
  );
}
