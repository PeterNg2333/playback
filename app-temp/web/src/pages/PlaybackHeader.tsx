import { useEffect, useState } from "react";
import type { PlaybackController } from "./handlers";
import { Header } from "../Component/Layout/Header";
import { Icon } from "../Component/Icon";
import { RecordingModeSchema } from "../types/api";

export function PlaybackHeader({ model }: { model: PlaybackController }) {
  const {
    session,
    navOpen,
    setSettingsOpen,
    setNavOpen,
    capture,
    busy,
    health,
    record,
    recordingMode,
    setRecordingMode,
  } = model;
  const [wave, setWave] = useState<number[]>(Array(20).fill(0));
  useEffect(() => {
    if (capture?.state !== "recording") {
      setWave(Array(20).fill(0));
    }
  }, [capture?.state]);
  useEffect(() => {
    const sample = (event: Event) => {
      const { level, streaming } = (
        event as CustomEvent<{ level: number; streaming: boolean }>
      ).detail;
      setWave((previous) => [...previous.slice(1), streaming ? level : 0]);
    };
    window.addEventListener("playback-capture-level", sample);
    return () => window.removeEventListener("playback-capture-level", sample);
  }, []);
  const streaming =
    capture?.activeSegments?.some((segment) => segment.streaming) ?? false;
  const isIdle = (capture?.state ?? "idle") === "idle";
  const isPaused = capture?.state === "paused";
  const chunkElapsedMs = Math.max(
    0,
    (capture?.capturedThroughMs ?? 0) - (capture?.lastFinalizedAtMs ?? 0),
  );
  const remaining = isIdle
    ? 30
    : Math.max(0, 30 - Math.floor(chunkElapsedMs / 1000));
  const countdown = `00:${String(remaining).padStart(2, "0")}`;
  const selectedMode = isIdle ? recordingMode : (capture?.sourceMode ?? "both");
  const sourceSelectionSupported = health?.recordingSourceSelection === true;
  const sourceSelectionTitle =
    health && !sourceSelectionSupported
      ? "Restart the API to choose a recording source"
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
            setSettingsOpen(false);
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
          disabled={!sourceSelectionSupported || !!busy || !isIdle}
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
              disabled={!session || !!busy || !health?.mongo}
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
                  aria-label={`Next audio part in ${remaining} seconds`}
                >
                  {countdown}
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
