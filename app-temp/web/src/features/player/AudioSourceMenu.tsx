import { Menu } from "../../Component/Menu";
import { audioSourceLabel } from "../recording/AudioSourceBadge";

type AudioSourceMenuProps = {
  sources: string[];
  sourceMode: string;
  selectedAudio: boolean;
  available: boolean;
  sessionPlaybackSupported: boolean;
  onPlaySession: () => void;
  onSelectSource: (source: string) => void;
};

export function AudioSourceMenu({
  sources,
  sourceMode,
  selectedAudio,
  available,
  sessionPlaybackSupported,
  onPlaySession,
  onSelectSource,
}: AudioSourceMenuProps) {
  const disabled = !available || !sessionPlaybackSupported;
  const label = selectedAudio
    ? "Selected audio"
    : sourceMode === "mix"
      ? "Both sources"
      : audioSourceLabel(sourceMode);
  let description = selectedAudio ? "Selected audio" : "Session audio";
  if (!selectedAudio && !sessionPlaybackSupported)
    description = "Restart API for session audio";

  return (
    <Menu
      className="player-mode"
      label="Playback mode"
      title={description}
      summary={label}
      dismissible
    >
      {(close) => (
        <div className="player-mode-menu">
          <span>{description}</span>
          <button
            className="player-session"
            type="button"
            aria-pressed={!selectedAudio}
            disabled={disabled}
            onClick={() => {
              onPlaySession();
              close();
            }}
          >
            Full session
          </button>
          <label>
            <span>Playback source</span>
            <select
              aria-label="Audio sources"
              value={sourceMode}
              disabled={disabled}
              onChange={(event) => {
                onSelectSource(event.target.value);
                close();
              }}
            >
              <option value="mix">Mix sources</option>
              {sources.map((source) => (
                <option key={source} value={source}>
                  {audioSourceLabel(source)}
                </option>
              ))}
            </select>
          </label>
        </div>
      )}
    </Menu>
  );
}
