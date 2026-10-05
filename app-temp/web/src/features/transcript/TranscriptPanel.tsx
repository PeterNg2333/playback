import clsx from "clsx";
import { Panel, PanelHeader } from "../../components/layout/Panel";
import { useHealth } from "../../lib/useHealth";
import { usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import type { AudioPlayer } from "../player/useAudioPlayer";
import { Timeline } from "./Timeline";
import { SessionSettings } from "./SessionSettings";
import { asrSummary } from "./asrStatus";

export function TranscriptPanel({
  className,
  session,
  player,
}: {
  className?: string;
  session: Session | null;
  player: AudioPlayer;
}) {
  const health = useHealth();
  const settingsOpen = usePlaybackStore((state) => state.settingsOpen);
  const status = asrSummary(session?.chunks ?? [], health?.asrPaused ?? false);
  return (
    <Panel className={className} data-testid="transcript-panel">
      {/* Raised above the timeline so the settings menu can overlap it. */}
      <PanelHeader
        className={clsx("relative", settingsOpen ? "z-20" : "z-9")}
        title={<h2 className="text-[15px] font-[750]">Transcript</h2>}
        actions={
          <>
            {status && (
              <span
                className="text-[11px] text-muted max-md:max-w-22.5 max-md:text-[9px]"
                role="status"
              >
                {status}
              </span>
            )}
            <SessionSettings session={session} />
          </>
        }
      />
      <div className="flex min-h-0 flex-1 overflow-hidden" id="transcript-view">
        <Timeline key={session?.id} session={session} player={player} />
      </div>
    </Panel>
  );
}
