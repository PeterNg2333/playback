import { useEffect, useMemo, useState } from "react";
import { useHealth } from "../../lib/useHealth";
import type { Session } from "../../lib/backend/schemas";
import type { AudioPlayer } from "./useAudioPlayer";
import { AudioSourceMenu } from "./AudioSourceMenu";

export function AudioPlayerBar({
  session,
  player,
}: {
  session: Session | null;
  player: AudioPlayer;
}) {
  const health = useHealth();
  const [position, setPosition] = useState(() => player.getPlaybackSnapshot());
  useEffect(() => {
    const update = () => {
      const next = player.getPlaybackSnapshot();
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
  }, [player.playingKey, session?.id, player.sourceMode]);

  const sources = useMemo(
    () => [...new Set(session?.chunks.map((chunk) => chunk.sourceId) || [])],
    [session?.chunks],
  );
  const available =
    session?.chunks.some((chunk) => chunk.status !== "silent") || false;
  const fullSessionSupported = health?.sessionAudioMix === true;
  const playerEnabled =
    available && (fullSessionSupported || position.kind === "row");
  if (!session) return null;
  const seekMaximum = Math.max(position.minimumMs + 1, position.maximumMs);
  const seekPosition = Math.max(
    position.minimumMs,
    Math.min(position.positionMs, seekMaximum),
  );

  return (
    <footer className="playback-footer" aria-label="Audio player">
      <div className="player-top">
        <AudioSourceMenu
          sources={sources}
          sourceMode={player.sourceMode}
          selectedAudio={position.kind === "row"}
          available={available}
          sessionPlaybackSupported={fullSessionSupported}
          onPlaySession={player.startSessionPlayback}
          onSelectSource={player.chooseSourceMode}
        />
        <input
          type="range"
          aria-label="Seek audio"
          min={position.minimumMs}
          max={seekMaximum}
          step="1000"
          value={seekPosition}
          disabled={!playerEnabled}
          onChange={(event) => player.seekPlayback(Number(event.target.value))}
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
            value={player.speed}
            onChange={(event) => player.chooseSpeed(Number(event.target.value))}
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
            onClick={() => player.skipPlayback(-5000)}
          >
            ↶ 5
          </button>
          <button
            type="button"
            className="player-primary"
            aria-label={position.playing ? "Pause audio" : "Play audio"}
            disabled={!playerEnabled}
            onClick={player.toggleCurrentPlayback}
          >
            {position.playing ? "Ⅱ" : "▶"}
          </button>
          <button
            type="button"
            aria-label="Forward 5 seconds"
            disabled={!playerEnabled}
            onClick={() => player.skipPlayback(5000)}
          >
            5 ↷
          </button>
        </div>
      </div>
    </footer>
  );
}
