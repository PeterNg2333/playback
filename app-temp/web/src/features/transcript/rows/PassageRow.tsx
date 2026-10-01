import { useEffect, useState } from "react";
import type { PassageEntry } from "../timelineEntries";
import type {
  Session,
  TermCandidate,
  TermInsight,
} from "../../../lib/backend/schemas";
import { askAboutTerm, captureTranscriptSelection } from "../../ask/askAbout";
import type { AudioPlayer } from "../../player/useAudioPlayer";
import { TranscriptRow } from "./TranscriptRow";
import { PlayButton } from "../../player/PlayButton";
import { AudioSourceBadge } from "../../recording/AudioSourceBadge";
import { recordedRange } from "../../../lib/time";
import { cleanAsrText } from "../asrStatus";
import { RowMeta, TimelineRow } from "./TimelineRow";

export function PassageRow({
  entry,
  session,
  playingKey,
  onTogglePlayback,
  revealId,
  insights,
  candidates,
}: {
  entry: PassageEntry;
  session: Session | null;
  playingKey: string | null;
  onTogglePlayback: AudioPlayer["togglePlayback"];
  revealId?: string;
  insights: Map<string, TermInsight[]>;
  candidates: Map<string, TermCandidate[]>;
}) {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (entry.transcripts.some((x) => x.id === revealId)) setOpen(true);
  }, [revealId, entry.transcripts]);
  const first = entry.transcripts[0],
    last = entry.transcripts.at(-1)!;
  const captureSelection = () => captureTranscriptSelection(session);
  const from = recordedRange(
    first.recordedAt,
    session?.createdAt,
    first.startMs,
    first.endMs,
  ).start;
  const through = recordedRange(
    last.recordedAt,
    session?.createdAt,
    last.startMs,
    last.endMs,
  ).end;
  return (
    <TimelineRow
      time={from}
      id={"passage-" + first.id}
      data-passage-sources={entry.transcripts.length}
    >
      <RowMeta>
        <AudioSourceBadge sourceId={first.sourceId} />
        <span>
          {from}–{through} · {entry.transcripts.length} original parts
        </span>
        <PlayButton
          id={"passage-" + first.id}
          chunks={entry.transcripts}
          startTime={from}
          endTime={through}
          playingKey={playingKey}
          onToggle={onTogglePlayback}
        />
      </RowMeta>
      <p
        className="my-2 leading-[1.7] whitespace-pre-wrap"
        onMouseUp={captureSelection}
        onKeyUp={captureSelection}
      >
        {entry.transcripts.map((text) => (
          <span key={text.id} data-transcript-id={text.id}>
            {cleanAsrText(text.displayOriginal ?? text.original)}{" "}
          </span>
        ))}
      </p>
      <details
        className="[&>article]:mt-2"
        open={open}
        onToggle={(event) => setOpen(event.currentTarget.open)}
      >
        <summary className="cursor-pointer text-[0.76rem] text-muted">
          Original parts, translations and individual audio
        </summary>
        {open &&
          entry.transcripts.map((transcript) => (
            <TranscriptRow
              key={transcript.id}
              transcript={transcript}
              session={session}
              playingKey={playingKey}
              onTogglePlayback={onTogglePlayback}
              onSelect={captureSelection}
              onAskTerm={(candidate, transcriptId) =>
                askAboutTerm(session, candidate, transcriptId)
              }
              reveal={transcript.id === revealId}
              insights={insights.get(transcript.id) ?? []}
              candidates={candidates.get(transcript.id) ?? []}
            />
          ))}
      </details>
    </TimelineRow>
  );
}
