import { usePlaybackStore } from "../../pages/store";
import type { Session, TermCandidate } from "../../types/api";

const MAX_SELECTED_CHARS = 1000;

// Uses the text the reader selected in one transcript passage as the source of the next question.
export function captureTranscriptSelection(session: Session | null) {
  const selected = window.getSelection();
  const text = selected?.toString().trim();
  const transcriptId =
    selected?.anchorNode?.parentElement?.closest<HTMLElement>(
      "[data-transcript-id]",
    )?.dataset.transcriptId;
  const transcript = session?.transcripts.find(
    (entry) => entry.id === transcriptId,
  );
  if (
    text &&
    text.length <= MAX_SELECTED_CHARS &&
    transcript &&
    (transcript.displayOriginal ?? transcript.original).includes(text)
  )
    usePlaybackStore.setState({
      selection: {
        transcriptId: transcript.id,
        text,
        startMs: transcript.startMs,
        endMs: transcript.endMs,
      },
    });
}

// Opens Ask with a question about a key term, citing where the term was heard or read.
export function askAboutTerm(
  session: Session | null,
  candidate: TermCandidate,
  transcriptId?: string,
) {
  const transcript =
    session?.transcripts.find((entry) => entry.id === transcriptId) ||
    session?.transcripts.find((entry) =>
      candidate.transcriptIds.includes(entry.id),
    );
  const matched = (transcript?.displayOriginal ?? transcript?.original)?.match(
    new RegExp(candidate.text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "i"),
  )?.[0];
  usePlaybackStore.setState({
    question: `Explain ${candidate.text} using the cited session sources.`,
    selection:
      transcript && matched
        ? {
            transcriptId: transcript.id,
            text: matched,
            startMs: transcript.startMs,
            endMs: transcript.endMs,
          }
        : null,
    focusMaterialId: candidate.materialIds[0] || null,
    chatOpen: true,
  });
}
