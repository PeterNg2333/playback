import clsx from "clsx";
import type {
  Chunk,
  Session,
  TermCandidate,
  Transcript,
  TermInsight,
} from "../../../lib/backend/schemas";
import { recordedRange } from "../../../lib/time";
import { cleanAsrText } from "../asrStatus";
import { PlayButton } from "../../player/PlayButton";
import { TermHighlight } from "../../terms/TermHighlight";
import { useLayoutEffect, useRef } from "react";
import { termSegments } from "../../terms/termMatching";
import {
  AudioSourceBadge,
  audioSourceLabel,
} from "../../recording/AudioSourceBadge";
import { Tag } from "../../../components/Tag";
import { RowDetails, RowLine, RowMeta, TimelineRow } from "./TimelineRow";

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
    <TimelineRow
      time={range.start}
      timeTitle={`${range.start}–${range.end}`}
      id={transcript.id}
      data-testid="transcript-row"
      data-transcript-id={transcript.id}
    >
      <RowMeta>
        <AudioSourceBadge sourceId={transcript.sourceId} />
        <span>
          {range.start}–{range.end}
        </span>
      </RowMeta>
      <RowLine>
        <RowDetails
          detailsRef={details}
          onSelectText={onSelect}
          summary={termSegments(original, insights).map((segment, index) =>
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
        >
          <span>
            {range.start}–{range.end}
          </span>
          <span className="mb-0.75 block text-[11px] font-bold text-muted">
            {audioSourceLabel(transcript.sourceId)}
          </span>
          {transcript.asrModel && (
            <small className="text-[smaller]">
              Recognized by {transcript.asrProvider} / {transcript.asrModel} ·
              hint {transcript.asrLanguageHint ?? "auto"}
            </small>
          )}
          {transcript.displayOriginal != null &&
            transcript.displayOriginal !== transcript.original && (
              <p className="my-0.75">
                Provider original: {transcript.original}
              </p>
            )}
          {transcript.uncertain && (
            <span className="mt-2 inline-block rounded-[5px] bg-pink-soft px-1.75 py-0.75 text-[10px] font-bold text-[#ac4c79]">
              Unclear · review audio
            </span>
          )}
          {transcript.recognitionStatus === "asr-empty" && (
            <span className="text-[11px] text-muted max-md:text-[9px]">
              ASR returned no text · audio retained
            </span>
          )}
          {transcript.revision && (
            // Shown only while translation is on, as before.
            <p
              className={clsx(
                "my-0.75 text-[11px] leading-[1.6] text-[#5e637b]",
                !session?.translationEnabled && "hidden",
              )}
            >
              Suggested revision: {transcript.revision}
            </p>
          )}
          {candidates.map((candidate) => (
            <Tag
              key={candidate.text}
              onClick={() => onAskTerm(candidate, transcript.id)}
              title="Ask Playback with this source"
            >
              {candidate.text}
            </Tag>
          ))}
        </RowDetails>
        <PlayButton
          id={transcript.id}
          chunks={[transcript]}
          startTime={range.start}
          endTime={range.end}
          playingKey={playingKey}
          onToggle={onTogglePlayback}
        />
      </RowLine>
      {session?.translationEnabled && transcript.original && (
        <p
          className={clsx(
            "mt-1 ml-4 text-[12px] leading-[1.5] text-[#5e637b]",
            !translationReady && "opacity-60",
          )}
        >
          {translationReady
            ? transcript.translation
            : transcript.translationStatus === "failed"
              ? "Translation failed · retry in settings"
              : "Translation pending…"}
        </p>
      )}
    </TimelineRow>
  );
}
