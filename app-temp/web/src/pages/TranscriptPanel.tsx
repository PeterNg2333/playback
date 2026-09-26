import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { Icon } from "../Component/Icon";
import { api } from "./api";
import { TranscriptContent } from "./TranscriptContent";

export function TranscriptPanel({ model }: { model: PlaybackController }) {
  const {
    session,
    health,
    settingsOpen,
    setSettingsOpen,
    busy,
    setTranslation,
    action,
    refresh,
  } = model;
  const chunks = session?.chunks || [];
  const asrStatus = health?.asrPaused
    ? "ASR paused · audio saved locally"
    : chunks.some((chunk) => chunk.status === "awaiting-consent")
    ? "ASR waiting · restart API"
    : chunks.some((chunk) => chunk.status === "asr-error")
      ? "ASR failed · retrying"
      : chunks.some((chunk) => chunk.status === "transcribing")
        ? "Transcribing…"
        : chunks.some((chunk) => chunk.status === "pending-asr")
          ? "ASR queued"
          : null;
  return (
    <Panel className="transcript-panel">
      <div className="panel-head">
        <h2>Transcript</h2>
        <div className="panel-actions">
          {asrStatus && <span className="processing-status" role="status">{asrStatus}</span>}
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
                    checked={!!session?.translationEnabled}
                    disabled={!session || !!busy}
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
        </div>
      </div>
      <TranscriptContent model={model} />
    </Panel>
  );
}
