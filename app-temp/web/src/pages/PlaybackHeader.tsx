import { useEffect, useState } from "react";
import type { PlaybackController } from "./handlers";
import { Header } from "../Component/Layout/Header";
import { Icon } from "../Component/Icon";

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
  } = model;
  const [wave, setWave] = useState<number[]>(Array(20).fill(0));
  useEffect(() => {
    if (capture?.state !== "recording") {
      setWave(Array(20).fill(0));
    }
  }, [capture?.state]);
  useEffect(() => {
    const sample = (event: Event) => {
      const { level, streaming } = (event as CustomEvent<{ level: number; streaming: boolean }>).detail;
      setWave((previous) => [...previous.slice(1), streaming ? level : 0]);
    };
    window.addEventListener("playback-capture-level", sample);
    return () => window.removeEventListener("playback-capture-level", sample);
  }, []);
  const streaming = capture?.activeSegments?.some((segment) => segment.streaming) ?? false;
  const remaining = capture?.state === "idle" ? 30 : Math.max(0,
    Math.ceil((30_000 - Math.max(0, (capture?.capturedThroughMs || 0) - (capture?.lastFinalizedAtMs || 0))) / 1000));
  const countdown = `00:${String(remaining).padStart(2, "0")}`;
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
        <div className="record-actions" data-state={capture?.state || "idle"}>
          {capture?.state === "idle" ? (
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
                {capture?.state === "paused" ? "Paused" : "Recording"}
                <span className="record-countdown" aria-label={`Next audio part in ${remaining} seconds`}>{countdown}</span>
              </span>
              <span
                className="capture-wave"
                data-active={streaming}
                role="img"
                aria-label={`Audio signal ${capture?.state === "paused" ? "paused" : streaming ? "received" : "quiet"}`}
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
                onClick={() =>
                  record(capture?.state === "paused" ? "resume" : "pause")
                }
                aria-label={
                  capture?.state === "paused"
                    ? "Resume recording"
                    : "Pause recording"
                }
              >
                <Icon name={capture?.state === "paused" ? "record" : "pause"} />
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
