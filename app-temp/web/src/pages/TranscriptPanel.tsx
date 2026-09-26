import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { Icon } from "../Component/Icon";
import { api } from "./api";
import { chunkStatus, time, transcriptDays } from "./format";
import { TranscriptRow } from "./TranscriptRow";

export function TranscriptPanel({ model }: { model: PlaybackController }) {
  const {
    view,
    setView,
    session,
    settingsOpen,
    setSettingsOpen,
    busy,
    setConsent,
    setTranslation,
    action,
    refresh,
    attach,
    askTerm,
    capture,
    seek,
    captureSelection,
  } = model;
  return (
    <Panel className="transcript-panel">
      <div className="panel-head">
        <h2>{view === "sources" ? "Sources" : "Transcript"}</h2>
        <div className="panel-actions">
          {view === "sources" && (
            <button
              className="text-control"
              onClick={() => setView("transcript")}
            >
              Back to transcript
            </button>
          )}
          {view === "transcript" && (
            <button
              className="text-control"
              onClick={() => {
                setSettingsOpen(false);
                setView("sources");
              }}
            >
              Sources
            </button>
          )}
          {view !== "sources" &&
            !!session?.chunks.filter(
              (c) => !["transcribed", "asr-empty", "silent"].includes(c.status),
            ).length && (
              <span className="processing-status">Processing audio…</span>
            )}
          {view !== "sources" && (
            <div className="settings-wrap">
              <button
                className="icon-control"
                aria-label="Transcript settings"
                aria-expanded={settingsOpen}
                onClick={() => setSettingsOpen(!settingsOpen)}
              >
                <Icon name="settings" />
              </button>
              {settingsOpen && (
                <div className="settings-menu">
                  <strong>Transcript settings</strong>
                  <label className="settings-check">
                    <input
                      type="checkbox"
                      checked={!!session?.externalProcessingConsent}
                      disabled={!session || !!busy}
                      onChange={(e) => setConsent(e.target.checked)}
                    />{" "}
                    I confirm lecturer, participants and institution consent,
                    and privacy and retention rules, for external processing.
                  </label>
                  <label className="settings-check">
                    <input
                      type="checkbox"
                      checked={!!session?.translationEnabled}
                      disabled={!session?.externalProcessingConsent || !!busy}
                      onChange={(e) => setTranslation(e.target.checked)}
                    />{" "}
                    啟用翻譯
                  </label>
                  <label htmlFor="translation-language">目標語言</label>
                  <select
                    id="translation-language"
                    value={session?.translationLanguage || "zh-Hant"}
                    disabled={!session || !!busy}
                    onChange={(e) =>
                      setTranslation(
                        !!session?.translationEnabled,
                        e.target.value,
                      )
                    }
                  >
                    <option value="zh-Hant">繁體中文</option>
                    <option value="en">English</option>
                    <option value="ja">日本語</option>
                    <option value="ko">한국어</option>
                  </select>
                  <small>
                    Applies to this session. Existing entries are translated in
                    the background; originals remain unchanged.
                  </small>
                  {session?.translationEnabled &&
                    session.transcripts.some(
                      (entry) => entry.translationStatus === "failed",
                    ) && (
                      <button
                        className="text-control"
                        disabled={!!busy}
                        onClick={() =>
                          action("translation", async () => {
                            await api(
                              `/sessions/${session!.id}/translation/retry`,
                              "POST",
                            );
                            await refresh(session!.id);
                          })
                        }
                      >
                        Retry failed translations
                      </button>
                    )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
      {view === "sources" ? (
        <div className="source-content">
          <button onClick={attach} disabled={!session || !!busy}>
            Attach text material
          </button>
          {session?.chunks.some(
            (chunk) =>
              !["transcribed", "asr-empty", "silent"].includes(chunk.status),
          ) && (
            <p className="asr-help">
              Saved audio remains linked to this session. With consent, failed
              recognition retries in the background.
            </p>
          )}
          {session?.materials.map((m) => (
            <article className="source-item" id={m.id} key={m.id}>
              <span className="source-icon">↗</span>
              <div>
                <strong>{m.name}</strong>
                <p>{m.text.slice(0, 180)}</p>
                {session.terms
                  .filter((candidate) => candidate.materialIds.includes(m.id))
                  .map((candidate) => (
                    <button
                      className="term-tag"
                      key={candidate.text}
                      onClick={() => askTerm(candidate)}
                    >
                      {candidate.text}
                    </button>
                  ))}
              </div>
            </article>
          ))}
          {session?.chunks.map((c) => (
            <article className="source-item" key={c.id}>
              <span className="source-icon">♫</span>
              <div>
                <strong>
                  {c.sourceId} · {time(c.startMs)}–{time(c.endMs)}
                </strong>
                <p>{chunkStatus(c.status)}</p>
                {c.error && (
                  <p className="capture-error" role="alert">
                    {c.error}
                  </p>
                )}
                <audio
                  controls
                  preload="none"
                  src={`/api/chunks/${c.id}/audio`}
                />
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="transcript-content">
          {capture?.error && (
            <p className="capture-error" role="alert">
              {capture.error}
            </p>
          )}
          <div className="rows">
            {session?.transcripts.length ? (
              Array.from(transcriptDays(session).entries()).map(
                ([day, hours]) => (
                  <details className="timeline-day" key={day} open>
                    <summary>{day}</summary>
                    {Array.from(hours.entries()).map(([hour, entries]) => (
                      <details className="timeline-hour" key={hour} open>
                        <summary>
                          {hour} · {entries.length} entries
                        </summary>
                        {entries.map((entry) => (
                          <TranscriptRow
                            key={entry.id}
                            transcript={entry}
                            session={session}
                            onSeek={seek}
                            onSelect={captureSelection}
                            onAskTerm={askTerm}
                          />
                        ))}
                      </details>
                    ))}
                  </details>
                ),
              )
            ) : (
              <p className="empty">
                No transcript yet. Record a session to see it here.
              </p>
            )}
          </div>
        </div>
      )}
    </Panel>
  );
}
