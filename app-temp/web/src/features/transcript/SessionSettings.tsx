import clsx from "clsx";
import { Icon } from "../../components/Icon";
import { IconButton } from "../../components/IconButton";
import { Button } from "../../components/Button";
import { useHealth } from "../../lib/useHealth";
import { useCaptureStatus } from "../recording/captureQuery";
import {
  retryTranslations,
  saveLanguages,
  saveTranslation,
} from "./saveSettings";
import {
  AsrLanguageSchema,
  NoteLanguageSchema,
  type Session,
} from "../../lib/backend/schemas";
import { usePlaybackField, usePlaybackStore } from "../../lib/store";

// The settings menu repeats one look for its labels, notes and drop-downs; the drop-downs keep
// the browser's own font and line height.
const labelStyle = "text-[11px] text-muted";
const noteStyle = "text-[10px] leading-[1.4] text-muted";
const selectStyle =
  "w-full rounded-[7px] border border-line bg-white p-1.75 text-[12px] text-ink [font-family:revert] [line-height:revert]";

export function SessionSettings({ session }: { session: Session | null }) {
  const health = useHealth();
  const busy = usePlaybackStore((state) => state.busy);
  const [settingsOpen, setSettingsOpen] = usePlaybackField("settingsOpen");
  const recordingThisSession = useCaptureStatus(
    (status) => status.state !== "idle" && status.sessionId === session?.id,
  );
  const streamActive = health?.asrStreaming && recordingThisSession;
  const setLanguages = (
    asrLanguage: Session["asrLanguage"],
    noteLanguage: Session["noteLanguage"],
    asrModel?: string | null,
  ) => {
    if (session)
      void saveLanguages(session, asrLanguage, noteLanguage, asrModel);
  };
  const setTranslation = (enabled: boolean, language?: string) => {
    if (session) void saveTranslation(session, enabled, language);
  };
  const translationsFailed =
    session?.transcripts.some(
      (entry) => entry.translationStatus === "failed",
    ) ?? false;
  const highlightedTerms =
    session?.termInsights?.filter((item) => item.highlight).length ?? 0;

  return (
    <div className="relative">
      <IconButton
        aria-label="Transcript settings"
        aria-expanded={settingsOpen}
        onClick={() => setSettingsOpen(!settingsOpen)}
      >
        <Icon name="settings" />
      </IconButton>
      {settingsOpen && (
        <div
          className="absolute top-10.5 right-0 z-30 grid max-h-[calc(100dvh-150px)] w-[min(310px,calc(100vw-26px))] gap-1.75 overflow-y-auto rounded-xl border border-line bg-white p-4 shadow-[0_14px_36px_#24263b1c]"
          data-testid="settings-menu"
        >
          <strong className="mb-1 text-[13px]">Transcript settings</strong>
          {health?.asr && (
            <small className={noteStyle}>
              ASR: {session?.asrModel ?? health.asr.model} (
              {health.asr.provider}, {health.asr.transport})
            </small>
          )}
          {!!health?.asrModels?.length && (
            <>
              <label className={labelStyle} htmlFor="asr-model">
                ASR model
              </label>
              <select
                className={selectStyle}
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
              <small className={noteStyle}>
                Tested 28 Sep: Qwen retained more mixed Cantonese; Whisper/Turbo
                sometimes rewrote it. All can mishear technical terms.
              </small>
            </>
          )}
          <small className={noteStyle}>
            {health?.asrStreaming
              ? "Streaming transcription"
              : "REST fallback · VAD utterances with short preview requests"}
            . Gray text is interim; saved transcripts replace it.
          </small>
          <label className={labelStyle} htmlFor="asr-language">
            ASR spoken language
          </label>
          <select
            className={selectStyle}
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
          <small className={noteStyle}>
            Applies to future ASR requests. For Cantonese and English, choose
            the mixed mode to keep automatic recognition.
            {streamActive &&
              " Stop recording to change streaming language or model."}
            {health?.asr &&
              !health.asr.supportsLanguageHint &&
              " This provider uses automatic recognition; the language hint is not applied."}
          </small>
          <label className={labelStyle} htmlFor="note-language">
            Notes output language
          </label>
          <select
            className={selectStyle}
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
          <small className={noteStyle}>
            Applies to the next AI revision. Use Revise with AI to update
            existing notes.
          </small>
          {health && !health.sessionLanguageSettings && (
            <small className={noteStyle}>
              Restart the API to save language settings.
            </small>
          )}
          <label
            className={clsx(
              labelStyle,
              "flex items-start gap-1.75 leading-[1.45]",
            )}
          >
            <input
              className="mt-0.5 mr-0.75 mb-0.75 ml-1 flex-none"
              type="checkbox"
              checked={!!session?.translationEnabled}
              disabled={!session || !!busy}
              onChange={(event) => setTranslation(event.target.checked)}
            />
            Enable translation
          </label>
          <label className={labelStyle} htmlFor="translation-language">
            Translation target language
          </label>
          <select
            className={selectStyle}
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
          <small className={noteStyle}>
            Applies to this session. Existing entries are translated in the
            background; originals remain unchanged.
          </small>
          {session?.translationEnabled && translationsFailed && (
            <Button
              disabled={!!busy}
              onClick={() => session && retryTranslations(session)}
            >
              Retry failed translations
            </Button>
          )}
          {!!session?.terms.length && (
            <div className="grid gap-1.5 border-t border-line pt-2">
              <small className={noteStyle}>
                {highlightedTerms} key terms highlighted
              </small>
              <small className={noteStyle}>
                Jev reviews confirmed source terms automatically. Selected terms
                receive saved AI explanations. Hover or tap a highlighted term
                to read it.
              </small>
              {!health?.jev && (
                <small className={noteStyle}>
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
