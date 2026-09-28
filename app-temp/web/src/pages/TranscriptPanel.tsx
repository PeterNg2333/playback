import type { KeyboardEvent } from "react";
import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { TranscriptContent } from "./TranscriptContent";
import { TranscriptSettings } from "./TranscriptSettings";
import { ActivityContent } from "./ActivityContent";
import { asrSummary } from "./format";

export function TranscriptPanel({ model }: { model: PlaybackController }) {
  const {
    session,
    health,
    transcriptView,
    setTranscriptView,
    setSettingsOpen,
  } = model;
  const asrStatus = asrSummary(
    session?.chunks ?? [],
    health?.asrPaused ?? false,
  );

  function selectView(view: typeof transcriptView) {
    setTranscriptView(view);
    setSettingsOpen(false);
  }

  function moveTab(event: KeyboardEvent<HTMLDivElement>) {
    let next: typeof transcriptView;
    switch (event.key) {
      case "Home":
        next = "transcript";
        break;
      case "End":
        next = "activity";
        break;
      case "ArrowLeft":
      case "ArrowRight":
        next = transcriptView === "transcript" ? "activity" : "transcript";
        break;
      default:
        return;
    }
    event.preventDefault();
    selectView(next);
    event.currentTarget.querySelector<HTMLElement>(`#${next}-tab`)?.focus();
  }

  return (
    <Panel className="transcript-panel">
      <div className="panel-head">
        <div
          className="transcript-tabs"
          role="tablist"
          aria-label="Transcript views"
          onKeyDown={moveTab}
        >
          <h2>
            <button
              type="button"
              role="tab"
              id="transcript-tab"
              tabIndex={transcriptView === "transcript" ? 0 : -1}
              aria-selected={transcriptView === "transcript"}
              aria-controls="transcript-view"
              onClick={() => selectView("transcript")}
            >
              Transcript
            </button>
          </h2>
          <button
            type="button"
            role="tab"
            id="activity-tab"
            tabIndex={transcriptView === "activity" ? 0 : -1}
            aria-selected={transcriptView === "activity"}
            aria-controls="activity-view"
            title="Read-only LLM edit log and Jev decisions"
            onClick={() => selectView("activity")}
          >
            Activity
          </button>
        </div>
        <div className="panel-actions">
          {asrStatus && transcriptView === "transcript" && (
            <span className="processing-status" role="status">
              {asrStatus}
            </span>
          )}
          <TranscriptSettings {...model} />
        </div>
      </div>
      {transcriptView === "transcript" ? (
        <div
          className="transcript-view"
          id="transcript-view"
          role="tabpanel"
          aria-labelledby="transcript-tab"
        >
          <TranscriptContent model={model} />
        </div>
      ) : (
        <div
          className="activity-view"
          id="activity-view"
          role="tabpanel"
          aria-labelledby="activity-tab"
        >
          <ActivityContent session={session} onSource={model.jump} />
        </div>
      )}
    </Panel>
  );
}
