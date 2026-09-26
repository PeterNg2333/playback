import type { Chunk, Session, TermCandidate, Transcript } from "../types/api";
import { cleanAsrText, time } from "./format";
import { RecordPlay } from "./RecordPlay";

export function TranscriptRow({
  transcript,
  session,
  playingKey,
  onTogglePlayback,
  onSelect,
  onAskTerm,
}: {
  transcript: Transcript;
  session: Session | null;
  playingKey: string | null;
  onTogglePlayback: (key: string, chunks: Pick<Chunk, "id">[]) => void;
  onSelect: () => void;
  onAskTerm: (candidate: TermCandidate, transcriptId?: string) => void;
}) {
  return (
    <article className="record-row transcript-row" id={transcript.id}>
      <span className="record-time" title={`${time(transcript.startMs)}–${time(transcript.endMs)}`}>
        {time(transcript.startMs)}
      </span>
      <details className="record-copy">
        <summary
          className="record-summary original"
          onMouseUp={onSelect}
          onKeyUp={onSelect}
        >
          {cleanAsrText(transcript.original) || "No words returned by ASR"}
        </summary>
        <div className="record-extra">
          <span>{time(transcript.startMs)}–{time(transcript.endMs)}</span>
          <span className="speaker">{transcript.sourceId}</span>
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
              <p className="translation">
                Translation: {transcript.translation}
              </p>
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
      </details>
      <RecordPlay
        id={transcript.id}
        chunks={[transcript]}
        startMs={transcript.startMs}
        endMs={transcript.endMs}
        playingKey={playingKey}
        onToggle={onTogglePlayback}
      />
    </article>
  );
}
