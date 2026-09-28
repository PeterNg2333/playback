import type { PlaybackController } from "./handlers";
import { Icon } from "../Component/Icon";

type TranscriptSettingsProps = Pick<
  PlaybackController,
  | "session"
  | "health"
  | "busy"
  | "settingsOpen"
  | "setSettingsOpen"
  | "setTranslation"
  | "retryTranslations"
  | "reviewTerms"
>;

export function TranscriptSettings({
  session,
  health,
  busy,
  settingsOpen,
  setSettingsOpen,
  setTranslation,
  retryTranslations,
  reviewTerms,
}: TranscriptSettingsProps) {
  const translationsFailed =
    session?.transcripts.some(
      (entry) => entry.translationStatus === "failed",
    ) ?? false;
  const reviewedTerms = new Set(
    session?.termInsights?.map((item) => item.term.toLocaleLowerCase()),
  );
  const hasUnreviewedTerms =
    session?.terms.some(
      (candidate) => !reviewedTerms.has(candidate.text.toLocaleLowerCase()),
    ) ?? false;
  const highlightedTerms =
    session?.termInsights?.filter((item) => item.highlight).length ?? 0;

  return (
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
              onChange={(event) => setTranslation(event.target.checked)}
            />
            啟用翻譯
          </label>
          <label htmlFor="translation-language">目標語言</label>
          <select
            id="translation-language"
            value={session?.translationLanguage || "zh-Hant"}
            disabled={!session || !!busy}
            onChange={(event) =>
              setTranslation(!!session?.translationEnabled, event.target.value)
            }
          >
            <option value="zh-Hant">繁體中文</option>
            <option value="en">English</option>
            <option value="ja">日本語</option>
            <option value="ko">한국어</option>
          </select>
          <small>
            Applies to this session. Existing entries are translated in the
            background; originals remain unchanged.
          </small>
          {session?.translationEnabled && translationsFailed && (
            <button
              className="text-control"
              disabled={!!busy}
              onClick={retryTranslations}
            >
              Retry failed translations
            </button>
          )}
          {!!session?.terms.length && (
            <div className="term-review-setting">
              <small>{highlightedTerms} key terms highlighted</small>
              <button
                className="text-control"
                disabled={!health?.jev || !!busy || !hasUnreviewedTerms}
                onClick={reviewTerms}
              >
                Review next key terms
              </button>
              <small>Sends up to 3 candidate terms to Jev.</small>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
