import { Workspace } from "../Component/Layout/Workspace";
import { usePlaybackController } from "./handlers";
import { PlaybackHeader } from "./PlaybackHeader";
import { SessionNav } from "./SessionNav";
import { NotesPanel } from "./NotesPanel";
import { TranscriptPanel } from "./TranscriptPanel";
import { PlaybackOverlay } from "./PlaybackOverlay";

export function PlaybackPage() {
  const model = usePlaybackController();
  const { view, setView, session, capture, audio, audioEnded } = model;
  return (
    <div
      className="app-shell"
      data-view={view}
      data-language={session?.translationEnabled ? "bilingual" : "original"}
    >
      <a className="skip" href="#workspace">
        Skip to workspace
      </a>
      <PlaybackHeader model={model} />
      {capture?.noSoundWarning && (
        <div className="sound-warning" role="alert">
          No microphone sound detected for over a minute. Check that your
          microphone is enabled and connected.
        </div>
      )}
      <SessionNav model={model} />
      <Workspace>
        <nav className="mobile-nav" aria-label="Workspace views">
          {(["notes", "transcript"] as const).map((v) => (
            <button
              key={v}
              aria-current={view === v ? "page" : undefined}
              onClick={() => setView(v)}
            >
              {v[0].toUpperCase() + v.slice(1)}
            </button>
          ))}
        </nav>
        <NotesPanel model={model} />
        <TranscriptPanel model={model} />
      </Workspace>
      <audio ref={audio} hidden onEnded={audioEnded} />
      <PlaybackOverlay model={model} />
    </div>
  );
}
