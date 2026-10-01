import { useEffect, useRef } from "react";
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
  const menu = useRef<HTMLDetailsElement>(null);
  const disabled = !available || !sessionPlaybackSupported;
  const label = selectedAudio
    ? "Selected audio"
    : sourceMode === "mix"
      ? "Both sources"
      : audioSourceLabel(sourceMode);
  let description = selectedAudio ? "Selected audio" : "Session audio";
  if (!selectedAudio && !sessionPlaybackSupported)
    description = "Restart API for session audio";

  function close() {
    if (menu.current) menu.current.open = false;
  }

  useEffect(() => {
    function closeOutside(event: PointerEvent) {
      const element = menu.current;
      if (element && !element.contains(event.target as Node))
        element.open = false;
    }
    function closeOnEscape(event: KeyboardEvent) {
      if (event.key !== "Escape" || !menu.current?.open) return;
      menu.current.open = false;
      menu.current.querySelector<HTMLElement>("summary")?.focus();
    }
    document.addEventListener("pointerdown", closeOutside);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, []);

  return (
    <details className="player-mode" ref={menu}>
      <summary aria-label="Playback mode" title={description}>
        {label}
      </summary>
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
    </details>
  );
}
