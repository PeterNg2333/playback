import type { Session, TermCandidate, Transcript } from "../types/api";
import { cleanAsrText, time } from "./format";

export function TranscriptRow({
  transcript,
  session,
  onSeek,
  onSelect,
  onAskTerm,
}: {
  transcript: Transcript;
  session: Session | null;
  onSeek: (entry: Transcript) => void;
  onSelect: () => void;
  onAskTerm: (candidate: TermCandidate, transcriptId?: string) => void;
}) {
  return (
    <article className="transcript-row" id={transcript.id} key={transcript.id}>
      <button
        className="time-range"
        onClick={() => onSeek(transcript)}
        aria-label={`Play audio from ${time(transcript.startMs)} to ${time(transcript.endMs)}`}
      >
        {time(transcript.startMs)}–{time(transcript.endMs)}
      </button>
      <div>
        <span className="speaker">{transcript.sourceId}</span>
        <p className="original" onMouseUp={onSelect} onKeyUp={onSelect}>
          {cleanAsrText(transcript.original) || "No words returned by ASR"}
        </p>
        {transcript.uncertain && (
          <span className="uncertain">Unclear · review audio</span>
        )}
        {transcript.recognitionStatus === "asr-empty" && (
          <span className="processing-status">
            ASR returned no text · audio retained
          </span>
        )}
        {session?.translationEnabled &&
          transcript.translationStatus === "completed" &&
          transcript.translationLanguage === session.translationLanguage &&
          transcript.translation && (
            <p className="translation">Translation: {transcript.translation}</p>
          )}
        {session?.translationEnabled &&
          transcript.original &&
          transcript.translationStatus === "failed" && (
            <span className="processing-status">
              Translation failed; retry available in settings
            </span>
          )}
        {transcript.revision && (
          <p className="translation">
            Suggested revision: {transcript.revision}
          </p>
        )}
        {session?.terms
          .filter((candidate) =>
            candidate.transcriptIds.includes(transcript.id),
          )
          .map((candidate) => (
            <button
              className="term-tag"
              key={candidate.text}
              onClick={() => onAskTerm(candidate, transcript.id)}
              title="Ask Playback with this source"
            >
              {candidate.text}
            </button>
          ))}
      </div>
    </article>
  );
}
