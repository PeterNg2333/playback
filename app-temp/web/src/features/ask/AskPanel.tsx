import clsx from "clsx";
import { useHealth } from "../../lib/useHealth";
import { recordedRange } from "../../lib/time";
import { usePlaybackField, usePlaybackStore } from "../../lib/store";
import type { Evidence, Session } from "../../lib/backend/schemas";
import { ChatAnswer } from "./ChatAnswer";
import { ChatConversationMenu } from "./ChatConversationMenu";
import { useAsk } from "./useAsk";
import { ChatError, ChatIconButton } from "./ChatControls";

// The Ask Playback button and panel: questions answered from the selected session's
// sources, optionally about a selected passage or material.
export function AskPanel({
  session,
  onOpenSource: jump,
}: {
  session: Session | null;
  onOpenSource: (source: Evidence) => void;
}) {
  const [selection, setSelection] = usePlaybackField("selection");
  const [chatOpen, setChatOpen] = usePlaybackField("chatOpen");
  const view = usePlaybackStore((state) => state.view);
  const [question, setQuestion] = usePlaybackField("question");
  const error = usePlaybackStore((state) => state.error);
  const [focusMaterialId, setFocusMaterialId] =
    usePlaybackField("focusMaterialId");
  const [useWeb, setUseWeb] = usePlaybackField("useWeb");
  const busy = usePlaybackStore((state) => state.busy);
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
          className="fixed right-3.5 bottom-34.5 z-10 max-w-[min(360px,calc(100vw-28px))] rounded-[9px] border border-accent bg-white px-3 py-2.25 text-[11px] font-bold text-accent shadow-[0_8px_22px_#44417e25] md:right-7 md:bottom-35"
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
      {!chatOpen ? (
        <button
          className={clsx(
            "fixed right-3.5 z-10 flex items-center gap-1.75 rounded-full bg-accent px-4 py-2.75 text-[12px] font-[750] text-white shadow-[0_8px_22px_#44417e36] md:right-7",
            // Below xl the notes tab ends in its own footer, so the button sits higher there.
            view === "notes"
              ? "bottom-32.5 md:bottom-20.5 xl:bottom-22.5"
              : "bottom-22.5",
          )}
          onClick={() => setChatOpen(true)}
        >
          ◇ Ask Playback
        </button>
      ) : (
        <section
          className={clsx(
            "fixed top-[max(75px,calc(100dvh-1210px))] right-3 bottom-22.5 z-10 flex max-h-280 w-[min(555px,calc(100vw-40px))] flex-col overflow-hidden rounded-[15px] border border-line bg-white shadow-[0_15px_45px_#27325f29] md:right-7",
            // Answers use the panel's whole width, with smaller headings than the notes.
            "**:data-markdown:max-w-full [&_[data-markdown]_:is(h1,h2,h3)]:text-[14px] [&_[data-markdown]_:is(ul,ol)]:pl-5",
          )}
          aria-label="Ask Playback"
        >
          <div className="flex shrink-0 items-center justify-between border-b border-line px-4 py-3.5">
            <div>
              <div className="flex items-center gap-2">
                <strong className="block text-[13px]">Ask Playback</strong>
                <ChatConversationMenu
                  session={session}
                  conversations={conversations}
                  onNewConversation={newConversation}
                  onSelectConversation={selectConversation}
                />
              </div>
              <small className="block text-[10px] text-muted">
                {session?.title ?? "Select a lecture session"} ·{" "}
                {conversations.current?.title ?? "New conversation"}
              </small>
            </div>
            <ChatIconButton
              onClick={() => setChatOpen(false)}
              aria-label="Close chat"
            >
              ×
            </ChatIconButton>
          </div>
          <div className="min-h-0 flex-1 overflow-auto p-3.75">
            {busy === "ask" && (
              <div className="py-2.5 text-accent" role="status">
                <strong>
                  Checking sources{useWeb ? " and public web" : ""}…
                </strong>
                {answerDraft && (
                  <p className="mb-4 whitespace-pre-wrap opacity-62">
                    Unverified draft ·{" "}
                    {answerDraft.replace(
                      "INSUFFICIENT_SOURCE",
                      "Lecture evidence is insufficient; checking the allowed sources…",
                    )}
                  </p>
                )}
              </div>
            )}
            {error && <ChatError>{error}</ChatError>}
            {!health ? (
              <p className={hintStyle} role="status">
                Backend connection is unavailable. Start the Playback server,
                then reload to reconnect. Your question stays here.
              </p>
            ) : (
              !health.gemini && (
                <p className={hintStyle}>
                  Gemini is unavailable; questions and translations can be
                  retried when configured.
                </p>
              )
            )}
            {conversations.error && (
              <ChatError>{conversations.error}</ChatError>
            )}
            {conversations.current?.turns.map((turn) => (
              <article className="[article+&]:mt-5" key={turn.id}>
                <p className="my-2.5 rounded-[10px] bg-[#f4f4f8] px-3 py-2.5 text-[12px] whitespace-pre-wrap">
                  {turn.question}
                </p>
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
                <p className={hintStyle}>
                  Ask about processed lecture content.
                </p>
              )
            )}
          </div>
          {(selection || focusMaterialId) && (
            <div className="flex shrink-0 items-center justify-between gap-2 border-t border-line bg-[#f7f8fb] px-3.25 py-2 text-[10px] text-muted">
              <span className="truncate">
                {selection
                  ? `Selected transcript · ${selectedTime?.start}–${selectedTime?.end} · “${selection.text}”`
                  : `Selected material · ${session?.materials.find((item) => item.id === focusMaterialId)?.name}`}
              </span>
              <button
                className="px-1.5 py-px text-[16px] text-muted"
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
          <label className="shrink-0 px-3.25 py-1.25 text-[10px] text-muted">
            <input
              type="checkbox"
              className="my-0.75 mr-0.75 ml-1"
              checked={useWeb}
              onChange={(e) => setUseWeb(e.target.checked)}
            />{" "}
            Include public web search (sends this question to Gemini Search)
          </label>
          <form
            className="flex shrink-0 gap-1.75 border-t border-line p-3"
            onSubmit={(e) => {
              e.preventDefault();
              ask();
            }}
          >
            <label className="sr-only" htmlFor="chat-input">
              Your question
            </label>
            {/* The browser's own field colours: black text, grey placeholder. */}
            <textarea
              id="chat-input"
              className="max-h-32.5 min-h-16 min-w-0 flex-1 resize-y rounded-lg border border-line bg-white p-2.5 text-[12px] text-black placeholder:text-[#757575]"
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
              className="rounded-lg bg-accent px-2.5 py-2 text-[11px] font-[750] text-white"
              disabled={
                !session || !!busy || conversations.loading || !question.trim()
              }
            >
              {busy === "ask" ? "Working…" : "Send"}
            </button>
          </form>
        </section>
      )}
    </>
  );
}

const hintStyle = "mb-2.5 text-[11px] text-muted";
