import { Icon } from "../../Component/Icon";
import { useHealth } from "../../lib/useHealth";
import { formatElapsed } from "../../lib/time";
import { usePlaybackField, usePlaybackStore } from "../../pages/store";
import { RecordingModeSchema, type Session } from "../../types/api";
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
  const selectedMode = isIdle ? recordingMode : (capture?.sourceMode ?? "both");
  const sourceSelectionSupported = health?.recordingSourceSelection === true;
  const sourceSelectionTitle =
    health && !sourceSelectionSupported
      ? "Restart the API before recording with the selected source"
      : "Choose which audio to record; stop recording to change it";
  return (
    <div className="top-actions">
      <select
        className="recording-mode"
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
      <div className="record-actions" data-state={capture?.state || "idle"}>
        {isIdle ? (
          <button
            className="record-start"
            disabled={
              !session || !!busy || !health?.mongo || !sourceSelectionSupported
            }
            onClick={() => record("start")}
            aria-label="Start recording"
          >
            <Icon name="record" />
            <span>Record</span>
          </button>
        ) : (
          <>
            <span className="record-indicator" role="status">
              <Icon name="record" />
              {isPaused ? "Paused" : "Recording"}
              <span
                className="record-countdown"
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
            <button
              className="icon-control"
              disabled={!!busy}
              onClick={() => record(isPaused ? "resume" : "pause")}
              aria-label={isPaused ? "Resume recording" : "Pause recording"}
            >
              <Icon name={isPaused ? "record" : "pause"} />
            </button>
            <button
              className="icon-control"
              disabled={!!busy}
              onClick={() => record("stop")}
              aria-label="Stop recording"
            >
              <Icon name="stop" />
            </button>
          </>
        )}
      </div>
    </div>
  );
}
