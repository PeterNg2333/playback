import { Workspace } from "../Component/Layout/Workspace";
import { usePlaybackController } from "./handlers";
import { PlaybackHeader } from "./PlaybackHeader";
import { SessionNav } from "./SessionNav";
import { NotesPanel } from "./NotesPanel";
import { TranscriptPanel } from "./TranscriptPanel";
import { PlaybackOverlay } from "./PlaybackOverlay";
import { PlaybackFooter } from "./PlaybackFooter";

export function PlaybackPage() {
  const model = usePlaybackController();
  const { view, setView, session, capture, audio, audioEnded, audioLoaded } =
    model;
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
      {model.workspaceLoading && (
        <div className="workspace-loading" role="status" aria-live="polite">
          Loading session…
        </div>
      )}
      {model.health && !model.health.recordingSourceSelection && (
        <div className="source-api-warning" role="status">
          Recording source selection needs the updated API. Stop the dev server
          with Ctrl+C, run pnpm.cmd dev again, then reload this page.
        </div>
      )}
      {capture?.noSoundWarning && (
        <div className="sound-warning" role="alert">
          No audio activity detected for over a minute. Check your microphone or
          system audio source.
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
      <audio
        ref={audio}
        hidden
        onLoadedMetadata={audioLoaded}
        onEnded={audioEnded}
      />
      <PlaybackFooter model={model} />
      <PlaybackOverlay model={model} />
    </div>
  );
}
