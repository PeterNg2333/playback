import { TextInputDialog } from "../Component/Dialog/TextInputDialog";
import { useHealth } from "../lib/useHealth";
import { useAsk } from "../features/ask/useAsk";
import { closeNameDialog, submitName } from "../features/library/useLibrary";
import type { Evidence, Session } from "../types/api";
import { usePlaybackField, usePlaybackStore } from "./store";
import { recordedRange } from "../lib/time";
import { ChatAnswer } from "./ChatAnswer";
import { ChatConversationMenu } from "./ChatConversationMenu";

export function PlaybackOverlay({
  session,
  onOpenSource: jump,
}: {
  session: Session | null;
  onOpenSource: (source: Evidence) => void;
}) {
  const [selection, setSelection] = usePlaybackField("selection");
  const [chatOpen, setChatOpen] = usePlaybackField("chatOpen");
  const [question, setQuestion] = usePlaybackField("question");
  const [error, setError] = usePlaybackField("error");
  const [focusMaterialId, setFocusMaterialId] =
    usePlaybackField("focusMaterialId");
  const [useWeb, setUseWeb] = usePlaybackField("useWeb");
  const busy = usePlaybackStore((state) => state.busy);
  const textDialog = usePlaybackStore((state) => state.textDialog);
  const textDialogError = usePlaybackStore((state) => state.textDialogError);
  const health = useHealth();
  const {
    conversations,
    answer,
    answerDraft,
    ask,
    newConversation,
    selectConversation,
  } = useAsk(session);
  const selectedTranscript = session?.transcripts.find(
    (entry) => entry.id === selection?.transcriptId,
  );
  const selectedTime =
    selection &&
    recordedRange(
      selectedTranscript?.recordedAt,
      session?.createdAt,
      selection.startMs,
      selection.endMs,
    );
  return (
    <>
      {selection && !chatOpen && (
        <button
          className="selection-action"
          onClick={() => {
            if (!question.trim())
              setQuestion(
                "Explain this selected passage using its source context.",
              );
            setChatOpen(true);
          }}
        >
          Ask Playback about selection · {selectedTime?.start}–
          {selectedTime?.end}
        </button>
      )}
      {error && !chatOpen && (
        <div className="global-error" role="alert">
          {error}
          <button aria-label="Dismiss error" onClick={() => setError("")}>
            ×
          </button>
        </div>
      )}
      {!chatOpen ? (
        <button className="floating" onClick={() => setChatOpen(true)}>
          ◇ Ask Playback
        </button>
      ) : (
        <section className="chat open" aria-label="Ask Playback">
          <div className="chat-head">
            <div>
              <div className="chat-title">
                <strong>Ask Playback</strong>
                <ChatConversationMenu
                  session={session}
                  conversations={conversations}
                  onNewConversation={newConversation}
                  onSelectConversation={selectConversation}
                />
              </div>
              <small>
                {session?.title ?? "Select a lecture session"} ·{" "}
                {conversations.current?.title ?? "New conversation"}
              </small>
            </div>
            <button
              className="icon-button"
              onClick={() => setChatOpen(false)}
              aria-label="Close chat"
            >
              ×
            </button>
          </div>
          <div className="chat-scroll">
            {busy === "ask" && (
              <div className="chat-progress" role="status">
                <strong>
                  Checking sources{useWeb ? " and public web" : ""}…
                </strong>
                {answerDraft && (
                  <p className="provisional-answer">
                    Unverified draft ·{" "}
                    {answerDraft.replace(
                      "INSUFFICIENT_SOURCE",
                      "Lecture evidence is insufficient; checking the allowed sources…",
                    )}
                  </p>
                )}
              </div>
            )}
            {error && (
              <p className="chat-error" role="alert">
                {error}
              </p>
            )}
            {!health ? (
              <p className="chat-hint" role="status">
                Backend connection is unavailable. Start the Playback server,
                then reload to reconnect. Your question stays here.
              </p>
            ) : (
              !health.gemini && (
                <p className="chat-hint">
                  Gemini is unavailable; questions and translations can be
                  retried when configured.
                </p>
              )
            )}
            {conversations.error && (
              <p className="chat-error" role="alert">
                {conversations.error}
              </p>
            )}
            {conversations.current?.turns.map((turn) => (
              <article className="chat-turn" key={turn.id}>
                <p className="chat-question">{turn.question}</p>
                <ChatAnswer
                  answer={turn.answer}
                  jump={jump}
                  sourceGroups={session?.sourceGroups}
                />
              </article>
            ))}
            {answer &&
            !conversations.current?.turns.some(
              (turn) =>
                !!answer.questionId &&
                turn.answer.questionId === answer.questionId,
            ) ? (
              <ChatAnswer
                answer={answer}
                jump={jump}
                sourceGroups={session?.sourceGroups}
              />
            ) : (
              !conversations.current?.turns.length &&
              !answer && (
                <p className="chat-hint">
                  Ask about processed lecture content.
                </p>
              )
            )}
          </div>
          {(selection || focusMaterialId) && (
            <div className="selected-source">
              <span>
                {selection
                  ? `Selected transcript · ${selectedTime?.start}–${selectedTime?.end} · “${selection.text}”`
                  : `Selected material · ${session?.materials.find((item) => item.id === focusMaterialId)?.name}`}
              </span>
              <button
                aria-label="Clear selected source"
                onClick={() => {
                  setSelection(null);
                  setFocusMaterialId(null);
                }}
              >
                ×
              </button>
            </div>
          )}
          <label className="web-toggle">
            <input
              type="checkbox"
              checked={useWeb}
              onChange={(e) => setUseWeb(e.target.checked)}
            />{" "}
            Include public web search (sends this question to Gemini Search)
          </label>
          <form
            className="chat-form"
            onSubmit={(e) => {
              e.preventDefault();
              ask();
            }}
          >
            <label className="skip" htmlFor="chat-input">
              Your question
            </label>
            <textarea
              id="chat-input"
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              placeholder="Ask a question…"
              rows={2}
              maxLength={1000}
              onKeyDown={(event) => {
                if (
                  event.key === "Enter" &&
                  !event.shiftKey &&
                  !event.nativeEvent.isComposing
                ) {
                  event.preventDefault();
                  if (session && !busy && question.trim()) ask();
                }
              }}
            />
            <button
              disabled={
                !session || !!busy || conversations.loading || !question.trim()
              }
            >
              {busy === "ask" ? "Working…" : "Send"}
            </button>
          </form>
        </section>
      )}
      <TextInputDialog
        open={textDialog !== null}
        title={
          textDialog?.kind === "session"
            ? "New session"
            : textDialog?.kind === "group"
              ? "New group"
              : "Rename group"
        }
        label={textDialog?.kind === "session" ? "Session title" : "Group name"}
        initialValue={
          textDialog?.kind === "rename-group" ? textDialog.current : ""
        }
        maxLength={textDialog?.kind === "session" ? 120 : 80}
        busy={busy === "dialog"}
        error={textDialogError}
        onCancel={closeNameDialog}
        onSubmit={submitName}
      />
    </>
  );
}
