import clsx from "clsx";
import { Icon } from "../../components/Icon";
import { IconButton } from "../../components/IconButton";
import { useHealth } from "../../lib/useHealth";
import { formatElapsed } from "../../lib/time";
import { usePlaybackField, usePlaybackStore } from "../../lib/store";
import {
  RecorderKindSchema,
  RecordingModeSchema,
  type Session,
} from "../../lib/backend/schemas";
import { LevelMeter } from "./LevelMeter";
import { useCapture } from "./useCapture";
import { useUploads } from "./uploadQueue";

const selectStyle =
  "rounded-lg border border-line bg-white px-2.5 py-1.75 text-[11px] font-bold text-ink disabled:opacity-60";

// Records the selected session: which recorder and audio to capture, start/pause/resume/stop,
// elapsed time, the input level and audio still waiting to upload.
export function Recorder({ session }: { session: Session | null }) {
  const [, setRecordingMode] = usePlaybackField("recordingMode");
  const [, setRecorderKind] = usePlaybackField("recorderKind");
  const busy = usePlaybackStore((state) => state.busy);
  const health = useHealth();
  const uploads = useUploads();
  const {
    status: capture,
    levels,
    record,
    kinds,
    kind,
    mode,
    systemAudio,
  } = useCapture(session);
  const streaming =
    capture?.activeSegments?.some((segment) => segment.streaming) ?? false;
  const isIdle = (capture?.state ?? "idle") === "idle";
  const isPaused = capture?.state === "paused";
  const elapsed =
    capture?.recordingElapsedMs == null
      ? "Restart API for timer"
      : formatElapsed(capture.recordingElapsedMs);
  const selectedMode = isIdle ? mode : (capture?.sourceMode ?? "microphone");
  const sourceSelectionSupported =
    kind === "browser" || health?.recordingSourceSelection === true;
  const sourceSelectionTitle =
    health && !sourceSelectionSupported
      ? "Restart the API before recording with the selected source"
      : kind === "browser" && systemAudio
        ? "Choose which audio to record. System audio asks you to share a screen: choose Entire screen and turn on “Also share system audio”"
        : kind === "browser"
          ? "This browser can record the microphone only"
          : "Choose which audio to record; stop recording to change it";
  const pendingTitle = uploads.error
    ? `${uploads.pending} recorded pieces are waiting to upload; retrying: ${uploads.error}`
    : `${uploads.pending} recorded pieces are uploading`;
  if (!kind)
    return (
      <span
        className="max-w-28 text-right text-[11px] text-muted md:max-w-none"
        title="This server cannot access your microphone or system audio, and this browser cannot record here. Open Playback over HTTPS in a current browser."
      >
        Recording unavailable
      </span>
    );
  return (
    <div
      className="flex flex-none items-center gap-1.25 md:gap-2.5"
      data-testid="recorder"
    >
      {uploads.pending > 0 && (
        <span
          className={clsx(
            "text-[11px] font-bold tabular-nums",
            uploads.error ? "text-[#a6456d]" : "text-muted",
          )}
          title={
            uploads.durable
              ? pendingTitle
              : `${pendingTitle}. This browser cannot keep them after the tab closes`
          }
          data-testid="upload-pending"
        >
          ↑{uploads.pending}
        </span>
      )}
      {kinds.length > 1 && (
        <select
          className={clsx(selectStyle, "max-w-22 md:max-w-32")}
          aria-label="Recorder"
          title="Browser: this tab records and uploads the audio. Windows (API): the API records the computer it runs on"
          disabled={!!busy || !isIdle}
          value={kind}
          onChange={(event) =>
            setRecorderKind(RecorderKindSchema.parse(event.target.value))
          }
        >
          <option value="browser">Browser</option>
          <option value="server">Windows (API)</option>
        </select>
      )}
      <select
        className={clsx(selectStyle, "max-w-27.5 md:max-w-46.25")}
        aria-label="Recording source"
        title={sourceSelectionTitle}
        disabled={!!busy || !isIdle}
        value={selectedMode}
        onChange={(event) =>
          setRecordingMode(RecordingModeSchema.parse(event.target.value))
        }
      >
        <option value="microphone">Microphone</option>
        <option value="system" disabled={!systemAudio}>
          System audio
        </option>
        <option value="both" disabled={!systemAudio}>
          Both sources
        </option>
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
            title={
              kind === "browser"
                ? "Record in this browser and upload the audio"
                : "Record on the computer running the API"
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
