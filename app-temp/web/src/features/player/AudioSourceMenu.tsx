import { Menu } from "../../components/Menu";
import { audioSourceLabel } from "../recording/AudioSourceBadge";
import { PlayerButton, PlayerSelect } from "./PlayerControls";

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
      className="relative min-w-0"
      summaryClassName="flex justify-between gap-1 rounded-[7px] border border-line p-1.25 text-[10px] font-bold whitespace-nowrap text-accent after:content-['▾'] md:px-1.75 md:text-[11px]"
      label="Playback mode"
      title={description}
      summary={label}
      dismissible
    >
      {(close) => (
        // Opens upwards, above the player bar.
        <div className="absolute bottom-[calc(100%+8px)] left-0 z-25 grid w-50 gap-2.25 rounded-[10px] border border-line bg-white p-3 text-[11px] shadow-[0_8px_28px_#25243c22]">
          <span className="text-muted">{description}</span>
          <PlayerButton
            className="px-2.25 py-1 text-[11px] aria-pressed:border-accent aria-pressed:bg-accent-soft aria-pressed:text-accent"
            type="button"
            aria-pressed={!selectedAudio}
            disabled={disabled}
            onClick={() => {
              onPlaySession();
              close();
            }}
          >
            Full session
          </PlayerButton>
          <label className="grid gap-1.25">
            <span>Playback source</span>
            <PlayerSelect
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
            </PlayerSelect>
          </label>
        </div>
      )}
    </Menu>
  );
}
