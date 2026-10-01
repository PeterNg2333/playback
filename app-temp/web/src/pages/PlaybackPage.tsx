import { useEffect } from "react";
import { Workspace } from "../components/layout/Workspace";
import { Header } from "../components/layout/Header";
import { Icon } from "../components/Icon";
import { ErrorToast } from "../components/ErrorToast";
import { useHealth } from "../lib/useHealth";
import { AskPanel } from "../features/ask/AskPanel";
import { NameDialog } from "../features/library/NameDialog";
import { refreshWorkspace } from "../features/library/refreshWorkspace";
import { useSelectedSession } from "../features/library/useSelectedSession";
import { useCaptureStatus } from "../features/recording/captureQuery";
import { Recorder } from "../features/recording/Recorder";
import { useRevealSource } from "../features/sources/useRevealSource";
import { useAudioPlayer } from "../features/player/useAudioPlayer";
import { showError, usePlaybackField, usePlaybackStore } from "../lib/store";
import { LibrarySidebar } from "../features/library/LibrarySidebar";
import { NotesPanel } from "../features/notes/NotesPanel";
import { TranscriptPanel } from "../features/transcript/TranscriptPanel";
import { AudioPlayerBar } from "../features/player/AudioPlayerBar";

export function PlaybackPage() {
  const session = useSelectedSession();
  const health = useHealth();
  const [view, setView] = usePlaybackField("view");
  const [navOpen, setNavOpen] = usePlaybackField("navOpen");
  const [error, setError] = usePlaybackField("error");
  const chatOpen = usePlaybackStore((state) => state.chatOpen);
  const workspaceLoading = usePlaybackStore((state) => state.workspaceLoading);
  const noSoundWarning = useCaptureStatus((status) => !!status.noSoundWarning);
  const player = useAudioPlayer(session, showError);
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
      <Header>
        <div className="brand">
          <button
            className="nav-toggle icon-control"
            aria-label="Toggle sessions"
            aria-expanded={navOpen}
            onClick={() => {
              usePlaybackStore.setState({ settingsOpen: false });
              setNavOpen(!navOpen);
            }}
          >
            <Icon name="menu" />
          </button>
          <span className="brand-icon">
            <Icon name="pulse" />
          </span>
          <span className="brand-title">Playback</span>
        </div>
        <h1 className="project-name">{session?.title || "Choose a session"}</h1>
        <Recorder session={session} />
      </Header>
      {workspaceLoading && (
        <div className="workspace-loading" role="status" aria-live="polite">
          Loading session…
        </div>
      )}
      {health && !health.recordingSourceSelection && (
        <div
          className="source-api-warning"
          role="status"
          data-testid="source-api-warning"
        >
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
      <LibrarySidebar session={session} />
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
      <AudioPlayerBar session={session} player={player} />
      <AskPanel session={session} onOpenSource={openSource} />
      {error && !chatOpen && (
        <ErrorToast message={error} onDismiss={() => setError("")} />
      )}
      <NameDialog />
    </div>
  );
}
