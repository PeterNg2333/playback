import type { PlaybackController } from "./handlers";
import { TextInputDialog } from "../Component/Dialog/TextInputDialog";
import { recordedRange } from "./format";

export function PlaybackOverlay({ model }: { model: PlaybackController }) {
  const {
    selection,
    chatOpen,
    setChatOpen,
    question,
    setQuestion,
    error,
    setError,
    answer,
    jump,
    focusMaterialId,
    setSelection,
    setFocusMaterialId,
    session,
    useWeb,
    setUseWeb,
    ask,
    busy,
    health,
    textDialog,
    textDialogError,
    closeTextDialog,
    submitTextDialog,
  } = model;
  const selectedTranscript = session?.transcripts.find((entry) => entry.id === selection?.transcriptId);
  const selectedTime = selection && recordedRange(selectedTranscript?.recordedAt, session?.createdAt, selection.startMs, selection.endMs);
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
          Ask Playback about selection · {selectedTime?.start}–{selectedTime?.end}
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
              <strong>Ask Playback</strong>
              <small>Private · processed session content</small>
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
            {error && (
              <p className="chat-error" role="alert">
                {error}
              </p>
            )}
            {!health?.gemini && (
              <p className="chat-hint">
                Gemini is unavailable; questions and translations can be retried
                when configured.
              </p>
            )}
            {answer ? (
              <div className="answer">
                <p>{answer.answer}</p>
                {answer.webAnswer && (
                  <p>
                    <strong>Public web:</strong> {answer.webAnswer}
                  </p>
                )}
                {answer.webSuggestions && (
                  <iframe
                    className="search-suggestions"
                    title="Google Search suggestions"
                    sandbox="allow-popups allow-popups-to-escape-sandbox"
                    srcDoc={answer.webSuggestions}
                  />
                )}
                {answer.inference && (
                  <small>Model inference · verify against sources</small>
                )}
                <div>
                  {answer.evidence?.map((ev, i) => (
                    <button
                      className="citation"
                      key={i}
                      onClick={() => jump(ev)}
                    >
                      {ev.title || ev.label || ev.kind}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <p className="chat-hint">Ask about processed lecture content.</p>
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
            <input
              id="chat-input"
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              placeholder="Ask a question…"
            />
            <button disabled={!session || !!busy}>Send</button>
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
        onCancel={closeTextDialog}
        onSubmit={submitTextDialog}
      />
    </>
  );
}
