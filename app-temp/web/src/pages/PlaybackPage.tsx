import clsx from "clsx";
import { useEffect } from "react";
import { Workspace } from "../components/layout/Workspace";
import { Header } from "../components/layout/Header";
import { Icon } from "../components/Icon";
import { IconButton } from "../components/IconButton";
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
  const recording = useCaptureStatus((status) => status.state !== "idle");
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
    <div className="flex h-dvh min-h-screen flex-col overflow-hidden">
      <a
        className="fixed -top-20 left-4 z-50 bg-white px-[0.8rem] py-2 [color:revert] underline focus:top-2"
        href="#workspace"
      >
        Skip to workspace
      </a>
      <Header>
        <div className="flex min-w-0 items-center gap-2.5">
          <IconButton
            className="md:hidden"
            aria-label="Toggle sessions"
            aria-expanded={navOpen}
            onClick={() => {
              usePlaybackStore.setState({ settingsOpen: false });
              setNavOpen(!navOpen);
            }}
          >
            <Icon name="menu" />
          </IconButton>
          <span className="grid size-7.25 flex-none place-items-center rounded-[10px] bg-[linear-gradient(135deg,#769bd1,#9c81c9_60%,#c475a5)] text-white md:size-8 [&_svg]:size-5.5">
            <Icon name="pulse" />
          </span>
          {/* Phones drop the name while recording, to leave room for the recorder. */}
          <span
            className={clsx(
              "truncate text-[14px] font-bold tracking-[-0.025em] max-xs:hidden md:text-[16px]",
              recording && "max-md:hidden",
            )}
          >
            Playback
          </span>
        </div>
        <h1 className="w-full max-w-full truncate text-center text-[11px] font-[650] text-ink max-md:justify-self-center max-xl:min-w-0 md:text-[13px] xl:absolute xl:left-1/2 xl:w-auto xl:max-w-[32vw] xl:-translate-x-1/2 xl:text-start">
          {session?.title || "Choose a session"}
        </h1>
        <Recorder session={session} />
      </Header>
      {workspaceLoading && (
        <div
          className="bg-[#eef5ff] px-4 py-[0.45rem] text-[0.85rem] text-[#24558b]"
          role="status"
          aria-live="polite"
        >
          Loading session…
        </div>
      )}
      {health && !health.recordingSourceSelection && (
        <div
          className="flex-none border-b border-line bg-[#fff8ed] px-5 py-1.75 text-[12px] text-ink md:ml-54 lg:ml-62"
          role="status"
          data-testid="source-api-warning"
        >
          Recording source selection needs the updated API. Stop the dev server
          with Ctrl+C, run pnpm.cmd dev again, then reload this page.
        </div>
      )}
      {noSoundWarning && (
        <div
          className="fixed top-17.5 right-5 z-25 max-w-95 rounded-[9px] border border-[#e7bf92] bg-[#fff8ed] px-3.75 py-3 text-[12px] text-[#744515] shadow-[0_8px_24px_#24263b15]"
          role="alert"
        >
          No audio activity detected for over a minute. Check your microphone or
          system audio source.
        </div>
      )}
      <LibrarySidebar session={session} />
      <Workspace>
        <nav
          className="z-5 grid w-full flex-none grid-cols-2 rounded-[9px] border border-line bg-white xl:hidden"
          aria-label="Workspace views"
        >
          {(["notes", "transcript"] as const).map((v) => (
            <button
              key={v}
              className="min-h-9.5 border-b-2 border-transparent px-0.5 py-2.5 text-[12px] font-bold text-muted aria-[current=page]:border-accent aria-[current=page]:text-accent md:px-1.5 md:py-px"
              aria-current={view === v ? "page" : undefined}
              onClick={() => setView(v)}
            >
              {v[0].toUpperCase() + v.slice(1)}
            </button>
          ))}
        </nav>
        {/* Below xl the two panels are tabs: only the chosen one shows. */}
        <NotesPanel
          className={clsx(view !== "notes" && "max-xl:hidden")}
          session={session}
          player={player}
          onOpenSource={openSource}
        />
        <TranscriptPanel
          className={clsx(view !== "transcript" && "max-xl:hidden")}
          session={session}
          player={player}
        />
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
