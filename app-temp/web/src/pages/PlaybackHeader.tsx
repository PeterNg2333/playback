import { Header } from "../Component/Layout/Header";
import { Icon } from "../Component/Icon";
import { useHealth } from "../lib/useHealth";
import { useCapture } from "../features/recording/useCapture";
import { RecordingModeSchema, type Session } from "../types/api";
import { usePlaybackField, usePlaybackStore } from "./store";
import { time } from "./format";

export function PlaybackHeader({ session }: { session: Session | null }) {
  const [navOpen, setNavOpen] = usePlaybackField("navOpen");
  const [recordingMode, setRecordingMode] = usePlaybackField("recordingMode");
  const busy = usePlaybackStore((state) => state.busy);
  const health = useHealth();
  const { status: capture, levels: wave, record } = useCapture(session);
  const streaming =
    capture?.activeSegments?.some((segment) => segment.streaming) ?? false;
  const isIdle = (capture?.state ?? "idle") === "idle";
  const isPaused = capture?.state === "paused";
  const elapsed =
    capture?.recordingElapsedMs == null
      ? "Restart API for timer"
      : time(capture.recordingElapsedMs);
  const selectedMode = isIdle ? recordingMode : (capture?.sourceMode ?? "both");
  const sourceSelectionSupported = health?.recordingSourceSelection === true;
  const sourceSelectionTitle =
    health && !sourceSelectionSupported
      ? "Restart the API before recording with the selected source"
      : "Choose which audio to record; stop recording to change it";
  const signalState = isPaused ? "paused" : streaming ? "received" : "quiet";
  return (
    <Header>
      <div className="brand">
        <button
          className="nav-toggle icon-control"
          aria-label="Toggle sessions"
          aria-expanded={navOpen}
          onClick={() => {
            usePlaybackStore.setState({ settingsOpen: false });
            setNavOpen(!navOpen);
          }}
        >
          <Icon name="menu" />
        </button>
        <span className="brand-icon">
          <Icon name="pulse" />
        </span>
        <span className="brand-title">Playback</span>
      </div>
      <h1 className="project-name">{session?.title || "Choose a session"}</h1>
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
                !session ||
                !!busy ||
                !health?.mongo ||
                !sourceSelectionSupported
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
              <span
                className="capture-wave"
                data-active={streaming}
                role="img"
                aria-label={`Audio signal ${signalState}`}
              >
                {wave.map((level, index) => (
                  <i
                    key={index}
                    style={{
                      height: `${3 + Math.round(Math.min(1, level / 250) * 18)}px`,
                    }}
                  />
                ))}
              </span>
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
    </Header>
  );
}
