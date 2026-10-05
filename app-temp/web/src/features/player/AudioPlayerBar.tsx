import { useEffect, useMemo, useState } from "react";
import { useHealth } from "../../lib/useHealth";
import type { Session } from "../../lib/backend/schemas";
import type { AudioPlayer } from "./useAudioPlayer";
import { AudioSourceMenu } from "./AudioSourceMenu";
import { PlayerButton, PlayerSelect } from "./PlayerControls";

const skipStyle = "min-h-7.5 min-w-9.75 px-2 py-0.75 text-[11px]";

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
    <footer
      className="grid flex-none gap-0.75 border-t border-line bg-white px-2.5 py-1.25 shadow-[0_-7px_24px_#24263b0b] md:ml-54 md:px-5 md:py-1.5 lg:ml-62"
      aria-label="Audio player"
    >
      <div className="grid grid-cols-[96px_minmax(20px,1fr)_auto_auto] items-center gap-1.25 md:grid-cols-[115px_minmax(28px,1fr)_auto_auto] md:gap-2.5">
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
          className="w-full min-w-0 cursor-pointer accent-accent"
          aria-label="Seek audio"
          min={position.minimumMs}
          max={seekMaximum}
          step="1000"
          value={seekPosition}
          disabled={!playerEnabled}
          onChange={(event) => player.seekPlayback(Number(event.target.value))}
        />
        <div
          className="flex gap-0.5 text-[9px] whitespace-nowrap text-muted tabular-nums md:gap-1.25 md:text-[11px]"
          aria-label="Playback time"
        >
          <time>{position.currentTime}</time>
          <span>/</span>
          <time>{position.endTime}</time>
        </div>
        <label>
          <span className="sr-only">Playback speed</span>
          <PlayerSelect
            aria-label="Playback speed"
            value={player.speed}
            onChange={(event) => player.chooseSpeed(Number(event.target.value))}
          >
            {[0.75, 1, 1.25, 1.5, 2].map((rate) => (
              <option key={rate} value={rate}>
                {rate}×
              </option>
            ))}
          </PlayerSelect>
        </label>
      </div>
      <div className="flex items-center justify-center">
        <div className="flex items-center gap-3">
          <PlayerButton
            type="button"
            className={skipStyle}
            aria-label="Back 5 seconds"
            disabled={!playerEnabled}
            onClick={() => player.skipPlayback(-5000)}
          >
            ↶ 5
          </PlayerButton>
          <PlayerButton
            primary
            type="button"
            className="min-h-8.5 min-w-8.5 px-2 py-0.75 text-[15px]"
            aria-label={position.playing ? "Pause audio" : "Play audio"}
            disabled={!playerEnabled}
            onClick={player.toggleCurrentPlayback}
          >
            {position.playing ? "Ⅱ" : "▶"}
          </PlayerButton>
          <PlayerButton
            type="button"
            className={skipStyle}
            aria-label="Forward 5 seconds"
            disabled={!playerEnabled}
            onClick={() => player.skipPlayback(5000)}
          >
            5 ↷
          </PlayerButton>
        </div>
      </div>
    </footer>
  );
}
