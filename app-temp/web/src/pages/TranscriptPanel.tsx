import { Panel } from "../Component/Layout/Panel";
import { useHealth } from "../lib/useHealth";
import type { Session } from "../types/api";
import type { AudioPlayer } from "./useAudioPlayback";
import { TranscriptContent } from "./TranscriptContent";
import { TranscriptSettings } from "./TranscriptSettings";
import { asrSummary } from "./format";

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
    <Panel className="transcript-panel">
      <div className="panel-head">
        <h2>Transcript</h2>
        <div className="panel-actions">
          {status && (
            <span className="processing-status" role="status">
              {status}
            </span>
          )}
          <TranscriptSettings session={session} />
        </div>
      </div>
      <div className="transcript-view" id="transcript-view">
        <TranscriptContent
          key={session?.id}
          session={session}
          player={player}
        />
      </div>
    </Panel>
  );
}
