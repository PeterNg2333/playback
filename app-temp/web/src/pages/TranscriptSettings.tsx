import type { PlaybackController } from "./handlers";
import { Icon } from "../Component/Icon";
import { AsrLanguageSchema, NoteLanguageSchema } from "../types/api";

type TranscriptSettingsProps = Pick<
  PlaybackController,
  | "session"
  | "health"
  | "busy"
  | "capture"
  | "settingsOpen"
  | "setSettingsOpen"
  | "setTranslation"
  | "setLanguages"
  | "retryTranslations"
>;

export function TranscriptSettings({
  session,
  health,
  busy,
  capture,
  settingsOpen,
  setSettingsOpen,
  setTranslation,
  setLanguages,
  retryTranslations,
}: TranscriptSettingsProps) {
  const streamActive =
    health?.asrStreaming &&
    capture?.state !== "idle" &&
    capture?.sessionId === session?.id;
  const translationsFailed =
    session?.transcripts.some(
      (entry) => entry.translationStatus === "failed",
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
          {health?.asr && (
            <small>
              ASR: {session?.asrModel ?? health.asr.model} (
              {health.asr.provider}, {health.asr.transport})
            </small>
          )}
          {!!health?.asrModels?.length && (
            <>
              <label htmlFor="asr-model">ASR model</label>
              <select
                id="asr-model"
                value={session?.asrModel ?? ""}
                disabled={!session || !!busy || !!streamActive}
                onChange={(event) =>
                  setLanguages(
                    session?.asrLanguage ?? "auto",
                    session?.noteLanguage ?? "zh-Hant",
                    event.target.value || null,
                  )
                }
              >
                <option value="">Default · {health.asr?.model}</option>
                {health.asrModels.map((model) => (
                  <option key={model} value={model}>
                    {model}
                  </option>
                ))}
              </select>
              <small>
                Tested 28 Sep: Qwen retained more mixed Cantonese; Whisper/Turbo
                sometimes rewrote it. All can mishear technical terms.
              </small>
            </>
          )}
          <small>
            {health?.asrStreaming
              ? "Streaming transcription"
              : "REST fallback · VAD utterances with short preview requests"}
            . Gray text is interim; saved transcripts replace it.
          </small>
          <label htmlFor="asr-language">ASR spoken language</label>
          <select
            id="asr-language"
            value={session?.asrLanguage ?? "auto"}
            disabled={
              !session ||
              !!busy ||
              !health?.sessionLanguageSettings ||
              !!streamActive
            }
            onChange={(event) =>
              setLanguages(
                AsrLanguageSchema.parse(event.target.value),
                session?.noteLanguage ?? "zh-Hant",
              )
            }
          >
            <option value="auto">Auto / mixed languages</option>
            <option value="yue-en">
              Cantonese + English (Traditional Chinese display)
            </option>
            <option value="yue">Cantonese (Traditional Chinese display)</option>
            <option value="zh">Mandarin</option>
            <option value="en">English</option>
          </select>
          <small>
            Applies to future ASR requests. For Cantonese and English, choose
            the mixed mode to keep automatic recognition.
            {streamActive &&
              " Stop recording to change streaming language or model."}
            {health?.asr &&
              !health.asr.supportsLanguageHint &&
              " This provider uses automatic recognition; the language hint is not applied."}
          </small>
          <label htmlFor="note-language">Notes output language</label>
          <select
            id="note-language"
            value={session?.noteLanguage ?? "zh-Hant"}
            disabled={!session || !!busy || !health?.sessionLanguageSettings}
            onChange={(event) =>
              setLanguages(
                session?.asrLanguage ?? "auto",
                NoteLanguageSchema.parse(event.target.value),
              )
            }
          >
            <option value="zh-Hant">TC · Traditional Chinese</option>
            <option value="zh-Hans">SC · Simplified Chinese</option>
            <option value="en">EN · English</option>
          </select>
          <small>
            Applies to the next AI revision. Use Revise with AI to update
            existing notes.
          </small>
          {health && !health.sessionLanguageSettings && (
            <small>Restart the API to save language settings.</small>
          )}
          <label className="settings-check">
            <input
              type="checkbox"
              checked={!!session?.translationEnabled}
              disabled={!session || !!busy}
              onChange={(event) => setTranslation(event.target.checked)}
            />
            Enable translation
          </label>
          <label htmlFor="translation-language">
            Translation target language
          </label>
          <select
            id="translation-language"
            value={session?.translationLanguage || "zh-Hant"}
            disabled={!session || !!busy}
            onChange={(event) =>
              setTranslation(!!session?.translationEnabled, event.target.value)
            }
          >
            <option value="yue-Hant">Cantonese (Traditional Chinese)</option>
            <option value="zh-Hant">TC · Traditional Chinese</option>
            <option value="zh-Hans">SC · Simplified Chinese</option>
            <option value="en">English</option>
            <option value="ja">Japanese</option>
            <option value="ko">Korean</option>
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
              <small>
                Jev reviews confirmed source terms automatically. Selected terms
                receive saved AI explanations. Hover or tap a highlighted term
                to read it.
              </small>
              {!health?.jev && (
                <small>
                  Term detection is unavailable until Jev is configured.
                </small>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
