import { useEffect, useState } from "react";
import type { PlaybackController } from "./handlers";
import { sourceLabel } from "./SourceTag";

export function PlaybackFooter({ model }: { model: PlaybackController }) {
  const [position, setPosition] = useState(() => model.getPlaybackSnapshot());
  useEffect(() => {
    const update = () => setPosition(model.getPlaybackSnapshot());
    update();
    const timer = setInterval(update, 250);
    return () => clearInterval(timer);
  }, [model.playingKey, model.session?.id, model.sourceMode]);

  const sources = [...new Set(model.session?.chunks.map((chunk) => chunk.sourceId) || [])];
  const available = model.session?.chunks.some((chunk) => chunk.status !== "silent") || false;
  const fullSessionSupported = model.health?.sessionAudioMix === true;
  const playerEnabled = available && (fullSessionSupported || position.kind === "row");
  if (!model.session) return null;

  return (
    <footer className="playback-footer" aria-label="Audio player">
      <div className="player-heading">
        <button
          className="player-session"
          type="button"
          aria-pressed={position.kind === "session"}
          disabled={!available || !fullSessionSupported}
          onClick={() => model.startSessionPlayback()}
        >
          Full session
        </button>
        <span>{position.kind === "row" ? "Selected audio" : fullSessionSupported ? "Session audio" : "Restart API for session audio"}</span>
        <label>
          <span className="sr-only">Audio sources</span>
          <select
            aria-label="Audio sources"
            value={model.sourceMode}
            disabled={!available || !fullSessionSupported}
            onChange={(event) => model.chooseSourceMode(event.target.value)}
          >
            <option value="mix">Mix sources</option>
            {sources.map((source) => (
              <option key={source} value={source}>{sourceLabel(source)}</option>
            ))}
          </select>
        </label>
      </div>
      <div className="player-seek">
        <time>{position.currentTime}</time>
        <input
          type="range"
          aria-label="Seek audio"
          min={position.minimumMs}
          max={Math.max(position.minimumMs + 1, position.maximumMs)}
          step="1000"
          value={Math.max(position.minimumMs, Math.min(position.positionMs, Math.max(position.minimumMs + 1, position.maximumMs)))}
          disabled={!playerEnabled}
          onChange={(event) => model.seekPlayback(Number(event.target.value))}
        />
        <time>{position.endTime}</time>
      </div>
      <div className="player-controls">
        <label>
          <span className="sr-only">Playback speed</span>
          <select aria-label="Playback speed" value={model.speed} onChange={(event) => model.chooseSpeed(Number(event.target.value))}>
            {[0.75, 1, 1.25, 1.5, 2].map((rate) => <option key={rate} value={rate}>{rate}×</option>)}
          </select>
        </label>
        <div>
          <button type="button" aria-label="Back 5 seconds" disabled={!playerEnabled} onClick={() => model.skipPlayback(-5000)}>↶ 5</button>
          <button type="button" className="player-primary" aria-label={position.playing ? "Pause audio" : "Play audio"} disabled={!playerEnabled} onClick={model.toggleCurrentPlayback}>{position.playing ? "Ⅱ" : "▶"}</button>
          <button type="button" aria-label="Forward 5 seconds" disabled={!playerEnabled} onClick={() => model.skipPlayback(5000)}>5 ↷</button>
        </div>
      </div>
    </footer>
  );
}
