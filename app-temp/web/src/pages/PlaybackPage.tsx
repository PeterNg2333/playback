import { useEffect } from "react";
import { Workspace } from "../Component/Layout/Workspace";
import { useHealth } from "../lib/useHealth";
import { refreshWorkspace } from "../features/library/refreshWorkspace";
import { useSelectedSession } from "../features/library/useSelectedSession";
import { useCaptureStatus } from "../features/recording/captureQuery";
import { useRevealSource } from "../features/sources/useRevealSource";
import { useAudioPlayback } from "./useAudioPlayback";
import { showError, usePlaybackField, usePlaybackStore } from "./store";
import { PlaybackHeader } from "./PlaybackHeader";
import { SessionNav } from "./SessionNav";
import { NotesPanel } from "./NotesPanel";
import { TranscriptPanel } from "./TranscriptPanel";
import { PlaybackOverlay } from "./PlaybackOverlay";
import { PlaybackFooter } from "./PlaybackFooter";

export function PlaybackPage() {
  const session = useSelectedSession();
  const health = useHealth();
  const [view, setView] = usePlaybackField("view");
  const workspaceLoading = usePlaybackStore((state) => state.workspaceLoading);
  const noSoundWarning = useCaptureStatus((status) => !!status.noSoundWarning);
  const player = useAudioPlayback(session, showError);
  const openSource = useRevealSource(session);
  const language = session?.translationEnabled ? "bilingual" : "original";

  useEffect(() => {
    refreshWorkspace().catch(showError);
  }, []);
  useEffect(() => {
    document.body.dataset.view = view;
    document.body.dataset.language = language;
  }, [view, language]);

  return (
    <div className="app-shell" data-view={view} data-language={language}>
      <a className="skip" href="#workspace">
        Skip to workspace
      </a>
      <PlaybackHeader session={session} />
      {workspaceLoading && (
        <div className="workspace-loading" role="status" aria-live="polite">
          Loading session…
        </div>
      )}
      {health && !health.recordingSourceSelection && (
        <div className="source-api-warning" role="status">
          Recording source selection needs the updated API. Stop the dev server
          with Ctrl+C, run pnpm.cmd dev again, then reload this page.
        </div>
      )}
      {noSoundWarning && (
        <div className="sound-warning" role="alert">
          No audio activity detected for over a minute. Check your microphone or
          system audio source.
        </div>
      )}
      <SessionNav session={session} />
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
        <NotesPanel
          session={session}
          player={player}
          onOpenSource={openSource}
        />
        <TranscriptPanel session={session} player={player} />
      </Workspace>
      <audio
        ref={player.audio}
        hidden
        onLoadedMetadata={player.audioLoaded}
        onEnded={player.audioEnded}
      />
      <PlaybackFooter session={session} player={player} />
      <PlaybackOverlay session={session} onOpenSource={openSource} />
    </div>
  );
}
