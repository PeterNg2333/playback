import { Panel } from "../../components/layout/Panel";
import { useHealth } from "../../lib/useHealth";
import type { Session } from "../../lib/backend/schemas";
import type { AudioPlayer } from "../player/useAudioPlayer";
import { Timeline } from "./Timeline";
import { SessionSettings } from "./SessionSettings";
import { asrSummary } from "./asrStatus";

export function TranscriptPanel({
  session,
  player,
}: {
  session: Session | null;
  player: AudioPlayer;
}) {
  const health = useHealth();
  const status = asrSummary(session?.chunks ?? [], health?.asrPaused ?? false);
  return (
    <Panel className="transcript-panel" data-testid="transcript-panel">
      <div className="panel-head">
        <h2>Transcript</h2>
        <div className="panel-actions">
          {status && (
            <span className="processing-status" role="status">
              {status}
            </span>
          )}
          <SessionSettings session={session} />
        </div>
      </div>
      <div className="transcript-view" id="transcript-view">
        <Timeline key={session?.id} session={session} player={player} />
      </div>
    </Panel>
  );
}
