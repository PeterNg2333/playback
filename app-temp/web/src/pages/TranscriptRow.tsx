import type {
  Chunk,
  Session,
  TermCandidate,
  Transcript,
} from "../types/api";
import { cleanAsrText, recordedRange } from "./format";
import { RecordPlay } from "./RecordPlay";
import { TermHighlight } from "./TermHighlight";
import { termSegments } from "../Component/termSegments";
import { SourceTag, sourceLabel } from "./SourceTag";

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
  onTogglePlayback: (key: string, chunks: Pick<Chunk, "id" | "startMs" | "endMs" | "recordedAt">[]) => void;
  onSelect: () => void;
  onAskTerm: (candidate: TermCandidate, transcriptId?: string) => void;
}) {
  const original =
    cleanAsrText(transcript.displayOriginal ?? transcript.original) || "No words returned by ASR";
  const range = recordedRange(transcript.recordedAt, session?.createdAt, transcript.startMs, transcript.endMs);
  const insights = (session?.termInsights || []).filter(
    (entry) => entry.highlight && entry.transcriptIds.includes(transcript.id) &&
      (!entry.outputLanguage || entry.outputLanguage === session?.noteLanguage),
  );
  const translationReady =
    session?.translationEnabled &&
    transcript.translationStatus === "completed" &&
    transcript.translationLanguage === session.translationLanguage &&
    !!transcript.translation;
  return (
    <article className="record-row transcript-row" id={transcript.id}>
      <span
        className="record-time"
        title={`${range.start}–${range.end}`}
      >
        {range.start}
      </span>
      <div className="record-main">
        <div className="record-meta">
          <SourceTag sourceId={transcript.sourceId} />
          <span>{range.start}–{range.end}</span>
        </div>
        <div className="record-line">
          <details className="record-copy">
            <summary
              className="record-summary original"
              onMouseUp={onSelect}
              onKeyUp={onSelect}
            >
              {termSegments(original, insights).map((segment, index) =>
                segment.term && session ? (
                  <TermHighlight key={index} sessionId={session.id} insight={segment.term} text={segment.text}
                    onAsk={() => onAskTerm({ text: segment.term!.term, transcriptIds: segment.term!.transcriptIds,
                      materialIds: segment.term!.materialIds }, transcript.id)} />
                ) : (
                  <span key={index}>{segment.text}</span>
                ),
              )}
            </summary>
            <div className="record-extra">
              <span>
                {range.start}–{range.end}
              </span>
              <span className="speaker">{sourceLabel(transcript.sourceId)}</span>
              {transcript.asrModel && <small>Recognized by {transcript.asrProvider} / {transcript.asrModel} · hint {transcript.asrLanguageHint ?? "auto"}</small>}
              {transcript.displayOriginal != null && transcript.displayOriginal !== transcript.original && <p className="raw-asr">Provider original: {transcript.original}</p>}
              {transcript.uncertain && (
                <span className="uncertain">Unclear · review audio</span>
              )}
              {transcript.recognitionStatus === "asr-empty" && (
                <span className="processing-status">
                  ASR returned no text · audio retained
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
            startTime={range.start}
            endTime={range.end}
            playingKey={playingKey}
            onToggle={onTogglePlayback}
          />
        </div>
        {session?.translationEnabled && transcript.original && (
          <p
            className={`translation ${translationReady ? "translation-completed" : "translation-pending"}`}
          >
            {translationReady
              ? transcript.translation
              : transcript.translationStatus === "failed"
                ? "Translation failed · retry in settings"
                : "Translation pending…"}
          </p>
        )}
      </div>
    </article>
  );
}
