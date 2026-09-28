import { useEffect, useRef, useState } from "react";
import type {
  Chunk,
  Session,
  TermCandidate,
  TermInsight,
  Transcript,
} from "../types/api";
import { cleanAsrText, recordedRange } from "./format";
import { RecordPlay } from "./RecordPlay";
import { TermExplanation } from "./TermExplanation";
import { SourceTag, sourceLabel } from "./SourceTag";

function highlightedSegments(text: string, insights: TermInsight[]) {
  const matches: { start: number; end: number; insight: TermInsight }[] = [];
  const lower = text.toLocaleLowerCase();
  for (const insight of insights) {
    const term = insight.term.toLocaleLowerCase();
    if (!term) continue;
    let from = 0;
    while (from < text.length && matches.length < 20) {
      const start = lower.indexOf(term, from);
      if (start < 0) break;
      matches.push({ start, end: start + term.length, insight });
      from = start + term.length;
    }
  }
  matches.sort((a, b) => a.start - b.start || b.end - a.end);
  const segments: { text: string; insight?: TermInsight }[] = [];
  let cursor = 0;
  for (const match of matches) {
    if (match.start < cursor) continue;
    if (match.start > cursor)
      segments.push({ text: text.slice(cursor, match.start) });
    segments.push({
      text: text.slice(match.start, match.end),
      insight: match.insight,
    });
    cursor = match.end;
  }
  if (cursor < text.length) segments.push({ text: text.slice(cursor) });
  return segments;
}

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
  const [openInsight, setOpenInsight] = useState<TermInsight | null>(null);
  const hold = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (hold.current) clearTimeout(hold.current);
    },
    [],
  );
  const cancelHold = () => {
    if (hold.current) clearTimeout(hold.current);
    hold.current = null;
  };
  const original =
    cleanAsrText(transcript.displayOriginal ?? transcript.original) || "No words returned by ASR";
  const range = recordedRange(transcript.recordedAt, session?.createdAt, transcript.startMs, transcript.endMs);
  const insights = (session?.termInsights || []).filter(
    (entry) => entry.highlight && entry.transcriptIds.includes(transcript.id),
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
              {highlightedSegments(original, insights).map((segment, index) =>
                segment.insight ? (
                  <button
                    type="button"
                    className="term-highlight"
                    key={index}
                    title="Hold for a short explanation"
                    aria-label={`Hold to explain ${segment.text}`}
                    onPointerDown={() => {
                      cancelHold();
                      hold.current = setTimeout(
                        () => setOpenInsight(segment.insight!),
                        500,
                      );
                    }}
                    onPointerUp={cancelHold}
                    onPointerCancel={cancelHold}
                    onPointerLeave={cancelHold}
                    onContextMenu={(event) => {
                      event.preventDefault();
                      setOpenInsight(segment.insight!);
                    }}
                    onClick={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                    }}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        event.stopPropagation();
                        setOpenInsight(segment.insight!);
                      }
                    }}
                  >
                    {segment.text}
                  </button>
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
        {openInsight && session && (
          <TermExplanation
            key={openInsight.id}
            sessionId={session.id}
            insight={openInsight}
            onClose={() => setOpenInsight(null)}
          />
        )}
      </div>
    </article>
  );
}
