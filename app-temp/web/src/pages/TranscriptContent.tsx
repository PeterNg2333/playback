import { memo, useCallback, useMemo, useRef, useState } from "react";
import type { CaptureStatus, TermCandidate, TermInsight } from "../types/api";
import { VirtualTranscript, type VirtualItem } from "./VirtualTranscript";
import type { PlaybackController } from "./handlers";
import type { TimelineEntry } from "./format";
import { chunkStatus, recordedRange, transcriptDays } from "./format";
import { TranscriptRow } from "./TranscriptRow";
import { RecordPlay } from "./RecordPlay";
import { SourceTag, sourceLabel } from "./SourceTag";
import { combineTranscriptEntries, type DisplayEntry } from "./transcriptPassages";
import { TranscriptPassage } from "./TranscriptPassage";

function sameTimelineCapture(a: CaptureStatus | null, b: CaptureStatus | null) {
  return a === b || a?.sessionId === b?.sessionId && a?.state === b?.state && a?.error === b?.error &&
    a?.interimError === b?.interimError && JSON.stringify(a?.activeSegments ?? []) === JSON.stringify(b?.activeSegments ?? []);
}

export const TranscriptContent = memo(function TranscriptContent({ model }: { model: PlaybackController }) {
  const {
    session,
    capture,
    health,
    attach,
    busy,
    workspaceLoading,
    askTerm,
    playingKey,
    togglePlayback,
    captureSelection,
    retryAsr,
  } = model;

  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [combine, setCombine] = useState(true);
  const [revealId, setRevealId] = useState<string>();
  const termIndex = useMemo(() => {
    const insights = new Map<string, TermInsight[]>(), candidates = new Map<string, TermCandidate[]>();
    for (const term of session?.termInsights ?? []) if (term.highlight && !["details", "detail", "okay", "information"].includes(term.term.toLowerCase()) &&
      (!term.outputLanguage || term.outputLanguage === session?.noteLanguage))
      for (const id of term.transcriptIds) insights.set(id, [...(insights.get(id) ?? []), term]);
    for (const term of session?.terms ?? []) for (const id of term.transcriptIds) candidates.set(id, [...(candidates.get(id) ?? []), term]);
    return { insights, candidates };
  }, [session?.termInsights, session?.terms, session?.noteLanguage]);
  const baseDays = useMemo(() => session ? transcriptDays(session) : new Map(),
    [session?.id, session?.createdAt, session?.transcripts, session?.chunks]);
  const renderCurrent = useRef(renderEntry); renderCurrent.current = renderEntry;
  const liveRows = useRef(new Map<string, Extract<TimelineEntry, { kind: "live" }>>());
  liveRows.current.clear();
  if (session && capture?.sessionId === session.id) for (const segment of capture.activeSegments ?? []) {
    if (session.transcripts.some(x => x.sourceId === segment.sourceId && x.startMs <= segment.startMs && x.endMs >= segment.endMs)) continue;
    liveRows.current.set("live-" + segment.sourceId + segment.startMs, { kind: "live", segment, at: new Date(segment.recordedAt) });
  }
  // Interim text/end time changes update the visible row through the ref above.
  // Rebuild the all-lecture layout only for confirmed data, collapse, or a new live window.
  const liveLayout = [...liveRows.current].map(([key, entry]) => key + entry.segment.recordedAt).join("|");
  const expandSource = useCallback((id: string) => {
    setRevealId(id);
    for (const [day, hours] of baseDays) for (const [hour, items] of hours)
      if (items.some((entry: TimelineEntry) => entry.kind === "transcript" ? entry.transcript.id === id : entry.kind !== "live" && entry.chunks.some(x => x.id === id)))
        setCollapsed(old => ({ ...old, [day]: false, [day + hour]: false }));
  }, [baseDays]);
  const entries = useMemo(() => {
  const entries: VirtualItem[] = [];
  const days = new Map<string, Map<string, TimelineEntry[]>>();
  for (const [day, hours] of baseDays) days.set(day, new Map([...hours].map(([hour, items]) => [hour, [...items]])));
  if (session && liveRows.current.size) {
    for (const [day, hours] of transcriptDays({ ...session, chunks: [], transcripts: [] }, [...liveRows.current.values()].map(entry => entry.segment))) {
      if (!days.has(day)) days.set(day, new Map());
      for (const [hour, items] of hours) days.get(day)!.set(hour, [...(days.get(day)!.get(hour) ?? []), ...items].sort((a, b) => a.at.getTime() - b.at.getTime()));
    }
  }
  for (const [day, hours] of [...days].sort(([a], [b]) => a.localeCompare(b))) {
    entries.push({ key: "day-" + day, sourceIds: [], estimate: 38, render: () => <button className="timeline-toggle" aria-expanded={!collapsed[day]}
      onClick={() => setCollapsed(old => ({ ...old, [day]: !old[day] }))}>{collapsed[day] ? "▸" : "▾"} {day}</button> });
    if (collapsed[day]) continue;
    for (const [hour, items] of [...hours].sort(([a], [b]) => a.localeCompare(b))) {
      const key = day + hour;
      entries.push({ key: "hour-" + key, sourceIds: [], estimate: 36, render: () => <button className="timeline-toggle hour-toggle" aria-expanded={!collapsed[key]}
        onClick={() => setCollapsed(old => ({ ...old, [key]: !old[key] }))}>{collapsed[key] ? "▸" : "▾"} {hour} · {items.length} parts</button> });
      if (collapsed[key]) continue;
      const latest = session?.transcripts.reduce((max, text) => Math.max(max, text.endMs), 0) ?? 0;
      const settled = capture?.sessionId === session?.id && capture?.state !== "idle" ? Math.floor((latest - 60000) / 30000) * 30000 : Infinity;
      for (const entry of combine ? combineTranscriptEntries(items, settled) : items) {
        const sourceIds = entry.kind === "passage" ? entry.transcripts.map(x => x.id) : entry.kind === "transcript" ? [entry.transcript.id] : entry.kind === "live" ? [] : entry.chunks.map(x => x.id);
        const key = entry.kind === "live" ? "live-" + entry.segment.sourceId + entry.segment.startMs : sourceIds[0];
        entries.push({ key, sourceIds, render: () => renderCurrent.current(liveRows.current.get(key) ?? entry) });
      }
    }
  }
  return entries;
  }, [baseDays, collapsed, liveLayout, combine, capture?.state]);
  function renderEntry(entry: DisplayEntry) {
    if (entry.kind === "passage") return <TranscriptPassage entry={entry} model={model} revealId={revealId}
      insights={termIndex.insights} candidates={termIndex.candidates} />;
    if (entry.kind === "live") {
      const { segment } = entry;
      const range = recordedRange(segment.recordedAt, session?.createdAt, segment.startMs, segment.endMs);
      return (
        <article className="record-row transcript-row audio-row live-segment" data-streaming={!!segment.streaming} key={`live-${segment.sourceId}-${segment.startMs}`} role="status" aria-label={`${segment.sourceId} audio recording in progress`}>
          <span className="record-time">{range.start}</span>
          <div className="record-main">
            <div className="record-meta"><SourceTag sourceId={segment.sourceId} /><span>Started {range.start}</span></div>
            <div className="record-line"><span className="record-summary"><span className="live-dot" /> {segment.interimText ? "Interim transcription" : segment.streaming ? "Speech active · recording audio" : "Speech detected · recording audio"}</span><span className="live-pending">{segment.interimText ? "Awaiting final…" : "Saving…"}</span></div>
            {segment.interimText && <p className="interim-text">{segment.interimText}</p>}
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
          insights={termIndex.insights.get(transcript.id) ?? []}
          candidates={termIndex.candidates.get(transcript.id) ?? []}
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
                return <div className="quiet-part" id={chunk.id} key={chunk.id}>
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
        id={first.id}
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
            disabled={!session || !!busy || workspaceLoading}
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
      {capture?.sessionId === session?.id && capture?.interimError && <p className="capture-error" role="status">{capture.interimError}</p>}
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
          <><label className="combine-transcript"><input type="checkbox" checked={combine} onChange={event => setCombine(event.target.checked)} />Combine completed passages</label>
            <VirtualTranscript key={session.id} items={entries} sessionId={session.id} onReveal={expandSource} /></>
        ) : capture?.sessionId !== session?.id || capture?.state === "idle" ? (
          <p className="empty">
            No transcript yet. Record a session to see audio and text here.
          </p>
        ) : null}
      </div>
    </div>
  );
}, (a, b) => a.model.session === b.model.session && sameTimelineCapture(a.model.capture, b.model.capture) && a.model.health === b.model.health &&
  a.model.busy === b.model.busy && a.model.workspaceLoading === b.model.workspaceLoading && a.model.playingKey === b.model.playingKey);
