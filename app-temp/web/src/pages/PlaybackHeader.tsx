import { useEffect, useState } from "react";
import type { PlaybackController } from "./handlers";
import { Header } from "../Component/Layout/Header";
import { Icon } from "../Component/Icon";
import { CaptureStatusSchema } from "../types/api";
import { api } from "./api";

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
      return;
    }
    let active = true;
    let inFlight = false;
    const sample = async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const status = await api("/capture/status", "GET", undefined, CaptureStatusSchema);
        if (active) {
          const level = Math.max(0, ...Object.values(status.levels || {}));
          setWave((previous) => [...previous.slice(1), level]);
        }
      } finally { inFlight = false; }
    };
    void sample().catch(() => {});
    const timer = setInterval(() => { void sample().catch(() => {}); }, 500);
    return () => { active = false; clearInterval(timer); };
  }, [capture?.state]);
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
              </span>
              <span
                className="capture-wave"
                role="img"
                aria-label={`Audio signal ${capture?.state === "paused" ? "paused" : wave.at(-1)! > 15 ? "received" : "quiet"}`}
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
