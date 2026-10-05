import clsx from "clsx";
import { Icon } from "../../components/Icon";
import { IconButton } from "../../components/IconButton";
import { useHealth } from "../../lib/useHealth";
import { formatElapsed } from "../../lib/time";
import { usePlaybackField, usePlaybackStore } from "../../lib/store";
import { RecordingModeSchema, type Session } from "../../lib/backend/schemas";
import { LevelMeter } from "./LevelMeter";
import { useCapture } from "./useCapture";

// Records the selected session: which audio to capture, start/pause/resume/stop,
// elapsed time and the input level.
export function Recorder({ session }: { session: Session | null }) {
  const [recordingMode, setRecordingMode] = usePlaybackField("recordingMode");
  const busy = usePlaybackStore((state) => state.busy);
  const health = useHealth();
  const { status: capture, levels, record } = useCapture(session);
  const streaming =
    capture?.activeSegments?.some((segment) => segment.streaming) ?? false;
  const isIdle = (capture?.state ?? "idle") === "idle";
  const isPaused = capture?.state === "paused";
  const elapsed =
    capture?.recordingElapsedMs == null
      ? "Restart API for timer"
      : formatElapsed(capture.recordingElapsedMs);
  const selectedMode = isIdle
    ? recordingMode
    : (capture?.sourceMode ?? "microphone");
  const sourceSelectionSupported = health?.recordingSourceSelection === true;
  const sourceSelectionTitle =
    health && !sourceSelectionSupported
      ? "Restart the API before recording with the selected source"
      : "Choose which audio to record; stop recording to change it";
  return (
    <div
      className="flex flex-none items-center gap-1.25 md:gap-2.5"
      data-testid="recorder"
    >
      <select
        className="max-w-27.5 rounded-lg border border-line bg-white px-2.5 py-1.75 text-[11px] font-bold text-ink disabled:opacity-60 md:max-w-46.25"
        aria-label="Recording source"
        title={sourceSelectionTitle}
        disabled={!!busy || !isIdle}
        value={selectedMode}
        onChange={(event) =>
          setRecordingMode(RecordingModeSchema.parse(event.target.value))
        }
      >
        <option value="microphone">Microphone</option>
        <option value="system">System audio</option>
        <option value="both">Both sources</option>
      </select>
      <div
        className={clsx(
          "flex items-center gap-1.75",
          !isIdle && "rounded-[10px] border py-1 pr-1.5 pl-2.5",
          !isIdle &&
            (isPaused
              ? "border-line bg-[#f4f5f8]"
              : "border-[#efcbdc] bg-[#fff3f8]"),
        )}
      >
        {isIdle ? (
          <button
            className="flex min-h-8.5 items-center gap-1.75 rounded-[9px] border border-[#eac9da] bg-[#fff7fa] px-2 text-[12px] font-bold text-[#a6456d] disabled:cursor-not-allowed disabled:opacity-50 md:px-3"
            disabled={
              !session || !!busy || !health?.mongo || !sourceSelectionSupported
            }
            onClick={() => record("start")}
            aria-label="Start recording"
          >
            <Icon name="record" className="size-3.5" />
            <span className="max-md:hidden">Record</span>
          </button>
        ) : (
          <>
            {/* Phones show only the dot and the time. */}
            <span
              className="flex items-center gap-1.5 text-[12px] font-bold text-[#a6456d] max-md:text-[0px]"
              role="status"
            >
              <Icon
                name="record"
                className={clsx(
                  "size-3.5",
                  !isPaused &&
                    "animate-record-pulse motion-reduce:animate-none",
                )}
              />
              {isPaused ? "Paused" : "Recording"}
              <span
                className="pl-1.25 text-[11px] tabular-nums"
                aria-label={`Recording elapsed ${elapsed}`}
              >
                {elapsed}
              </span>
            </span>
            <LevelMeter
              levels={levels}
              speaking={streaming}
              paused={isPaused}
            />
            <IconButton
              disabled={!!busy}
              onClick={() => record(isPaused ? "resume" : "pause")}
              aria-label={isPaused ? "Resume recording" : "Pause recording"}
            >
              <Icon name={isPaused ? "record" : "pause"} />
            </IconButton>
            <IconButton
              disabled={!!busy}
              onClick={() => record("stop")}
              aria-label="Stop recording"
            >
              <Icon name="stop" />
            </IconButton>
          </>
        )}
      </div>
    </div>
  );
}
