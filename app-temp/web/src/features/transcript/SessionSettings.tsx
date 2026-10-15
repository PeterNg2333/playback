import clsx from "clsx";
import { useRef } from "react";
import { useDismiss } from "../../components/Menu";
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
  "w-full rounded-[7px] border border-line bg-white p-1.75 text-[12px] text-ink font-[revert] leading-[revert]";

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
  const popup = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useDismiss(popup, settingsOpen, (escape) => {
    setSettingsOpen(false);
    if (escape) trigger.current?.focus();
  });

  return (
    <div className="relative" ref={popup}>
      <IconButton
        ref={trigger}
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
          {!!health?.asrModels?.length && (
            <>
              <label className={labelStyle} htmlFor="asr-model">
                Transcription model
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
            </>
          )}
          <label className={labelStyle} htmlFor="asr-language">
            Spoken language
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
            <option value="yue-en">Cantonese + English</option>
            <option value="yue">Cantonese</option>
            <option value="zh">Mandarin</option>
            <option value="en">English</option>
          </select>
          {streamActive && (
            <small className={noteStyle}>
              Stop recording to change model or language.
            </small>
          )}
          {health?.asr && !health.asr.supportsLanguageHint && (
            <small className={noteStyle}>
              This model detects the spoken language automatically.
            </small>
          )}
          <label className={labelStyle} htmlFor="note-language">
            Notes language
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
          <small className={noteStyle}>Used for the next AI revision.</small>
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

          {session?.translationEnabled && translationsFailed && (
            <Button
              disabled={!!busy}
              onClick={() => session && retryTranslations(session)}
            >
              Retry failed translations
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
