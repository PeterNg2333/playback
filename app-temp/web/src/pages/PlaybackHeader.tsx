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
