import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { TranscriptContent } from "./TranscriptContent";
import { TranscriptSettings } from "./TranscriptSettings";
import { asrSummary } from "./format";

export function TranscriptPanel({ model }: { model: PlaybackController }) {
  const status = asrSummary(model.session?.chunks ?? [], model.health?.asrPaused ?? false);
  return <Panel className="transcript-panel">
    <div className="panel-head">
      <h2>Transcript</h2>
      <div className="panel-actions">
        {status && <span className="processing-status" role="status">{status}</span>}
        <TranscriptSettings {...model} />
      </div>
    </div>
    <div className="transcript-view" id="transcript-view"><TranscriptContent key={model.session?.id} model={model} /></div>
  </Panel>;
}
