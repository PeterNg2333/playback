import type { PlaybackController } from "./handlers";
import type { TimelineEntry } from "./format";
import { chunkStatus, recordedRange, transcriptDays } from "./format";
import { TranscriptRow } from "./TranscriptRow";
import { RecordPlay } from "./RecordPlay";
import { SourceTag, sourceLabel } from "./SourceTag";

export function TranscriptContent({ model }: { model: PlaybackController }) {
  const {
    session,
    capture,
    health,
    attach,
    busy,
    askTerm,
    playingKey,
    togglePlayback,
    captureSelection,
    retryAsr,
  } = model;

  function renderEntry(entry: TimelineEntry) {
    if (entry.kind === "live") {
      const { segment } = entry;
      const range = recordedRange(segment.recordedAt, session?.createdAt, segment.startMs, segment.endMs);
      return (
        <article className="record-row transcript-row audio-row live-segment" data-streaming={!!segment.streaming} key={`live-${segment.sourceId}-${segment.startMs}`} role="status" aria-label={`${segment.sourceId} audio recording in progress`}>
          <span className="record-time">{range.start}</span>
          <div className="record-main">
            <div className="record-meta"><SourceTag sourceId={segment.sourceId} /><span>Started {range.start}</span></div>
            <div className="record-line"><span className="record-summary"><span className="live-dot" /> {segment.streaming ? "Speech active · recording audio" : "Speech detected · recording audio"}</span><span className="live-pending">Saving…</span></div>
          </div>
        </article>
      );
    }
    const transcript =
      entry.kind === "transcript"
        ? entry.transcript
        : entry.kind === "audio"
          ? entry.transcript
          : undefined;
    if (transcript) {
      return (
        <TranscriptRow
          key={transcript.id}
          transcript={transcript}
          session={session}
          playingKey={playingKey}
          onTogglePlayback={togglePlayback}
          onSelect={captureSelection}
          onAskTerm={askTerm}
        />
      );
    }
    if (entry.kind === "transcript") return null;
    const chunks = entry.chunks;
    const first = chunks[0];
    const last = chunks.at(-1)!;
    const startTime = recordedRange(first.recordedAt, session?.createdAt, first.startMs, first.endMs).start;
    const endTime = recordedRange(last.recordedAt, session?.createdAt, last.startMs, last.endMs).end;
    const range = { start: startTime, end: endTime };
    if (entry.kind === "silence") {
      const emptyIds = chunks.filter((chunk) => chunk.status === "asr-empty").map((chunk) => chunk.id);
      return (
        <details className="quiet-section" key={first.id}>
          <summary><span className="quiet-rule" /><span>No audio · {chunks.length} parts</span><span className="quiet-rule" /></summary>
          <div className="quiet-section-body">
            <span>{emptyIds.length ? `${emptyIds.length} parts returned no words from ASR. ` : ""}Saved audio from {Array.from(new Set(chunks.map((chunk) => sourceLabel(chunk.sourceId)))).join(" + ")}</span>
            {emptyIds.length > 0 && <button className="retry-asr" disabled={!!busy || health?.asrPaused} onClick={() => retryAsr(emptyIds)}>Retry ASR</button>}
            <div className="quiet-parts">
              {chunks.map((chunk) => {
                const part = recordedRange(chunk.recordedAt, session?.createdAt, chunk.startMs, chunk.endMs);
                return <div className="quiet-part" key={chunk.id}>
                  <SourceTag sourceId={chunk.sourceId} />
                  <span>{part.start}–{part.end}</span>
                  <RecordPlay id={chunk.id} chunks={[chunk]} startTime={part.start} endTime={part.end} playingKey={playingKey} onToggle={togglePlayback} />
                </div>;
              })}
            </div>
          </div>
        </details>
      );
    }
    const failedIds = chunks
      .filter((chunk) => chunk.status === "asr-manual")
      .map((chunk) => chunk.id);
    const retryIds = failedIds;
    const label = failedIds.length
      ? "ASR stopped · audio saved"
      : chunkStatus(first.status);
    return (
      <article
        className={`record-row transcript-row audio-row ${failedIds.length ? "asr-manual-row" : ""}`}
        key={first.id}
      >
        <span className="record-time">{range.start}</span>
        <div className="record-main">
          <div className="record-meta">
            <SourceTag sourceId={first.sourceId} />
            <span>{range.start}–{range.end}</span>
          </div>
          <div className="record-line">
            <details className="record-copy">
              <summary className="record-summary">{label}</summary>
              <div className="record-extra">
                <span>
                  {range.start}–{range.end}
                </span>
                <span className="speaker">{sourceLabel(first.sourceId)}</span>
                {chunks.length > 1 && (
                  <p>{chunks.length} audio parts grouped</p>
                )}
                {first.error && (
                  <p className="capture-error" role="alert">
                    {first.error}
                  </p>
                )}
              </div>
            </details>
            <RecordPlay
              id={first.id}
              chunks={chunks}
              startTime={range.start}
              endTime={range.end}
              playingKey={playingKey}
              onToggle={togglePlayback}
            />
          </div>
          {retryIds.length > 0 && (
            <button
              className="retry-asr"
              disabled={!!busy || health?.asrPaused}
              onClick={() => retryAsr(retryIds)}
            >
              Retry ASR for {retryIds.length} saved audio part
              {retryIds.length === 1 ? "" : "s"}
            </button>
          )}
        </div>
      </article>
    );
  }

  return (
    <div className="transcript-content">
      <details className="materials-section" id="session-materials">
        <summary>
          Text materials <span>{session?.materials.length || 0}</span>
        </summary>
        <div className="materials-content">
          <button
            className="text-control"
            onClick={attach}
            disabled={!session || !!busy}
          >
            Attach text material
          </button>
          {session?.materials.map((material) => (
            <article
              className="material-item"
              id={material.id}
              key={material.id}
            >
              <strong>{material.name}</strong>
              <p>{material.text.slice(0, 180)}</p>
              {session.terms
                .filter((candidate) =>
                  candidate.materialIds.includes(material.id),
                )
                .map((candidate) => (
                  <button
                    className="term-tag"
                    key={candidate.text}
                    onClick={() => askTerm(candidate)}
                  >
                    {candidate.text}
                  </button>
                ))}
            </article>
          ))}
        </div>
      </details>
      {capture?.error && (
        <p className="capture-error" role="alert">
          {capture.error}
        </p>
      )}
      <div className="rows">
        {capture &&
          capture.sessionId === session?.id &&
          capture.state !== "idle" &&
          !(capture.activeSegments?.length) && (
            <div className="live-audio-row" data-state={capture.state} role="status">
              <span className="live-dot" />
              <span>
                {capture.state === "paused"
                  ? "Recording paused"
                  : "Listening for sound"}
              </span>
            </div>
          )}
        {session && (session.chunks.length || session.transcripts.length || (capture?.sessionId === session.id && !!capture.activeSegments?.length)) ? (
          Array.from(transcriptDays(session, capture?.sessionId === session.id ? capture.activeSegments || [] : []).entries()).map(([day, hours]) => (
            <details className="timeline-day" key={day} open>
              <summary>{day}</summary>
              {Array.from(hours.entries()).map(([hour, entries]) => (
                <details className="timeline-hour" key={hour} open>
                  <summary>
                    {hour} · {entries.length} sections
                  </summary>
                  <div className="timeline-entries">{entries.map(renderEntry)}</div>
                </details>
              ))}
            </details>
          ))
        ) : capture?.sessionId !== session?.id || capture?.state === "idle" ? (
          <p className="empty">
            No transcript yet. Record a session to see audio and text here.
          </p>
        ) : null}
      </div>
    </div>
  );
}
