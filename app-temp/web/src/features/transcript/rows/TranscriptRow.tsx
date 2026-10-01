import type {
  Chunk,
  Session,
  TermCandidate,
  Transcript,
  TermInsight,
} from "../../../types/api";
import { recordedRange } from "../../../lib/time";
import { cleanAsrText } from "../asrStatus";
import { PlayButton } from "../../player/PlayButton";
import { TermHighlight } from "../../../pages/TermHighlight";
import { useLayoutEffect, useRef } from "react";
import { termSegments } from "../../../Component/termSegments";
import {
  AudioSourceBadge,
  audioSourceLabel,
} from "../../recording/AudioSourceBadge";

export function TranscriptRow({
  transcript,
  session,
  playingKey,
  onTogglePlayback,
  onSelect,
  onAskTerm,
  insights,
  candidates,
  reveal = false,
}: {
  transcript: Transcript;
  session: Session | null;
  playingKey: string | null;
  onTogglePlayback: (
    key: string,
    chunks: Pick<Chunk, "id" | "startMs" | "endMs" | "recordedAt">[],
  ) => void;
  onSelect: () => void;
  onAskTerm: (candidate: TermCandidate, transcriptId?: string) => void;
  insights: TermInsight[];
  candidates: TermCandidate[];
  reveal?: boolean;
}) {
  const details = useRef<HTMLDetailsElement>(null);
  useLayoutEffect(() => {
    if (reveal && details.current) details.current.open = true;
  }, [reveal]);
  const original =
    cleanAsrText(transcript.displayOriginal ?? transcript.original) ||
    "No words returned by ASR";
  const range = recordedRange(
    transcript.recordedAt,
    session?.createdAt,
    transcript.startMs,
    transcript.endMs,
  );
  const translationReady =
    session?.translationEnabled &&
    transcript.translationStatus === "completed" &&
    transcript.translationLanguage === session.translationLanguage &&
    !!transcript.translation;
  return (
    <article
      className="record-row transcript-row"
      id={transcript.id}
      data-transcript-id={transcript.id}
    >
      <span className="record-time" title={`${range.start}–${range.end}`}>
        {range.start}
      </span>
      <div className="record-main">
        <div className="record-meta">
          <AudioSourceBadge sourceId={transcript.sourceId} />
          <span>
            {range.start}–{range.end}
          </span>
        </div>
        <div className="record-line">
          <details className="record-copy" ref={details}>
            <summary
              className="record-summary original"
              onMouseUp={onSelect}
              onKeyUp={onSelect}
            >
              {termSegments(original, insights).map((segment, index) =>
                segment.term && session ? (
                  <TermHighlight
                    key={index}
                    sessionId={session.id}
                    insight={segment.term}
                    text={segment.text}
                    onAsk={() =>
                      onAskTerm(
                        {
                          text: segment.term!.term,
                          transcriptIds: segment.term!.transcriptIds,
                          materialIds: segment.term!.materialIds,
                        },
                        transcript.id,
                      )
                    }
                  />
                ) : (
                  <span key={index}>{segment.text}</span>
                ),
              )}
            </summary>
            <div className="record-extra">
              <span>
                {range.start}–{range.end}
              </span>
              <span className="speaker">
                {audioSourceLabel(transcript.sourceId)}
              </span>
              {transcript.asrModel && (
                <small>
                  Recognized by {transcript.asrProvider} / {transcript.asrModel}{" "}
                  · hint {transcript.asrLanguageHint ?? "auto"}
                </small>
              )}
              {transcript.displayOriginal != null &&
                transcript.displayOriginal !== transcript.original && (
                  <p className="raw-asr">
                    Provider original: {transcript.original}
                  </p>
                )}
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
              {candidates.map((candidate) => (
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
          <PlayButton
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
