import { useEffect, useMemo, useState } from "react";
import type { PlaybackController } from "./handlers";
import { PlaybackModeMenu } from "./PlaybackModeMenu";

export function PlaybackFooter({ model }: { model: PlaybackController }) {
  const [position, setPosition] = useState(() => model.getPlaybackSnapshot());
  useEffect(() => {
    const update = () => {
      const next = model.getPlaybackSnapshot();
      setPosition((old) =>
        old.active === next.active &&
        old.playing === next.playing &&
        old.kind === next.kind &&
        old.positionMs === next.positionMs &&
        old.minimumMs === next.minimumMs &&
        old.maximumMs === next.maximumMs &&
        old.currentTime === next.currentTime &&
        old.endTime === next.endTime
          ? old
          : next,
      );
    };
    update();
    const timer = setInterval(update, 250);
    return () => clearInterval(timer);
  }, [model.playingKey, model.session?.id, model.sourceMode]);

  const sources = useMemo(
    () => [
      ...new Set(model.session?.chunks.map((chunk) => chunk.sourceId) || []),
    ],
    [model.session?.chunks],
  );
  const available =
    model.session?.chunks.some((chunk) => chunk.status !== "silent") || false;
  const fullSessionSupported = model.health?.sessionAudioMix === true;
  const playerEnabled =
    available && (fullSessionSupported || position.kind === "row");
  if (!model.session) return null;
  const seekMaximum = Math.max(position.minimumMs + 1, position.maximumMs);
  const seekPosition = Math.max(
    position.minimumMs,
    Math.min(position.positionMs, seekMaximum),
  );

  return (
    <footer className="playback-footer" aria-label="Audio player">
      <div className="player-top">
        <PlaybackModeMenu
          sources={sources}
          sourceMode={model.sourceMode}
          selectedAudio={position.kind === "row"}
          available={available}
          sessionPlaybackSupported={fullSessionSupported}
          onPlaySession={model.startSessionPlayback}
          onSelectSource={model.chooseSourceMode}
        />
        <input
          type="range"
          aria-label="Seek audio"
          min={position.minimumMs}
          max={seekMaximum}
          step="1000"
          value={seekPosition}
          disabled={!playerEnabled}
          onChange={(event) => model.seekPlayback(Number(event.target.value))}
        />
        <div className="player-times" aria-label="Playback time">
          <time>{position.currentTime}</time>
          <span>/</span>
          <time>{position.endTime}</time>
        </div>
        <label>
          <span className="sr-only">Playback speed</span>
          <select
            aria-label="Playback speed"
            value={model.speed}
            onChange={(event) => model.chooseSpeed(Number(event.target.value))}
          >
            {[0.75, 1, 1.25, 1.5, 2].map((rate) => (
              <option key={rate} value={rate}>
                {rate}×
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="player-controls">
        <div>
          <button
            type="button"
            aria-label="Back 5 seconds"
            disabled={!playerEnabled}
            onClick={() => model.skipPlayback(-5000)}
          >
            ↶ 5
          </button>
          <button
            type="button"
            className="player-primary"
            aria-label={position.playing ? "Pause audio" : "Play audio"}
            disabled={!playerEnabled}
            onClick={model.toggleCurrentPlayback}
          >
            {position.playing ? "Ⅱ" : "▶"}
          </button>
          <button
            type="button"
            aria-label="Forward 5 seconds"
            disabled={!playerEnabled}
            onClick={() => model.skipPlayback(5000)}
          >
            5 ↷
          </button>
        </div>
      </div>
    </footer>
  );
}
