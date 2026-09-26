import type { PlaybackController } from "./handlers";
import { chunkStatus, time, transcriptDays } from "./format";
import { TranscriptRow } from "./TranscriptRow";
import { RecordPlay } from "./RecordPlay";

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
  } = model;
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
        {session && (session.chunks.length || session.transcripts.length) ? (
          Array.from(transcriptDays(session).entries()).map(([day, hours]) => (
            <details className="timeline-day" key={day} open>
              <summary>{day}</summary>
              {Array.from(hours.entries()).map(([hour, entries]) => (
                <details className="timeline-hour" key={hour} open>
                  <summary>
                    {hour} · {entries.length} sections
                  </summary>
                  {entries.map((entry) => {
                    if (entry.kind === "silence") {
                      return (
                        <details
                          className="silence-section"
                          key={entry.chunks[0].id}
                        >
                          <summary>
                            <span>No sound section</span>
                            <small>
                              {entry.chunks[0].sourceId} ·{" "}
                              {time(entry.chunks[0].startMs)}–
                              {time(entry.chunks.at(-1)!.endMs)}
                            </small>
                          </summary>
                          <div className="silence-audio">
                            <RecordPlay
                              id={entry.chunks[0].id}
                              chunks={entry.chunks}
                              startMs={entry.chunks[0].startMs}
                              endMs={entry.chunks.at(-1)!.endMs}
                              playingKey={playingKey}
                              onToggle={togglePlayback}
                            />
                            <span>
                              {entry.chunks.length} audio part
                              {entry.chunks.length === 1 ? "" : "s"}
                            </span>
                          </div>
                        </details>
                      );
                    }
                    if (entry.kind === "transcript" || entry.transcript) {
                      return (
                        <TranscriptRow
                          key={entry.transcript!.id}
                          transcript={entry.transcript!}
                          session={session}
                          playingKey={playingKey}
                          onTogglePlayback={togglePlayback}
                          onSelect={captureSelection}
                          onAskTerm={askTerm}
                        />
                      );
                    }
                    const chunk = entry.chunks[0];
                    return (
                      <article
                        className="record-row transcript-row audio-row"
                        key={chunk.id}
                      >
                        <span className="record-time">
                          {time(chunk.startMs)}–
                          {time(entry.chunks.at(-1)!.endMs)}
                        </span>
                        <details className="record-copy">
                          <summary className="record-summary">
                            {health?.asrPaused && chunk.status === "asr-error"
                              ? "ASR failed · paused"
                              : chunkStatus(chunk.status)}
                          </summary>
                          <div className="record-extra">
                            <span className="speaker">{chunk.sourceId}</span>
                            {entry.chunks.length > 1 && (
                              <p>
                                {entry.chunks.length} consecutive audio parts
                              </p>
                            )}
                            {chunk.error && (
                              <p className="capture-error" role="alert">
                                {chunk.error}
                              </p>
                            )}
                          </div>
                        </details>
                        <RecordPlay
                          id={chunk.id}
                          chunks={entry.chunks}
                          startMs={chunk.startMs}
                          endMs={entry.chunks.at(-1)!.endMs}
                          playingKey={playingKey}
                          onToggle={togglePlayback}
                        />
                      </article>
                    );
                  })}
                </details>
              ))}
            </details>
          ))
        ) : (
          <p className="empty">
            No transcript yet. Record a session to see audio and text here.
          </p>
        )}
      </div>
    </div>
  );
}
