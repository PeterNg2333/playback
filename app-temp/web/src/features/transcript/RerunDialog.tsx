import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { Segment, Segmented } from "../../components/Segmented";
import { useHealth } from "../../lib/useHealth";
import { usePlaybackField, usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { useCaptureStatus } from "../recording/captureQuery";
import { rerunAudioMinutes, startRerun, type RerunInput } from "./rerun";

const labelStyle = "text-[12px] font-bold";
const noteStyle = "text-[11px] leading-[1.45] text-muted";
const selectStyle =
  "w-full rounded-lg border border-line bg-white px-3 py-2 text-[13px] text-ink";
const windowChoices = [30, 45, 60, 80];
const noteModes: { value: RerunInput["notes"]; label: string; note: string }[] =
  [
    {
      value: "incremental",
      label: "Update",
      note: "Only blocks that cite re-transcribed speech are corrected; locked blocks stay.",
    },
    {
      value: "rebuild",
      label: "Rebuild",
      note: "The notes are written again from the whole transcript. Locked blocks stay, and every earlier version remains in history.",
    },
    { value: "none", label: "Leave", note: "Notes are not changed now." },
  ];

// Rerun a session: re-recognize the saved audio in longer windows, then update or rebuild the notes.
// "notes" mode offers only the notes rebuild.
export function RerunDialog({ session }: { session: Session | null }) {
  const [mode, setMode] = usePlaybackField("rerunDialog");
  const busy = usePlaybackStore((state) => state.busy);
  const health = useHealth();
  const recording = useCaptureStatus(
    (status) => status.state !== "idle" && status.sessionId === session?.id,
  );
  const dialog = useRef<HTMLDialogElement>(null);
  const [asrModel, setAsrModel] = useState<string>("");
  const [windowSeconds, setWindowSeconds] = useState(45);
  const [notes, setNotes] = useState<RerunInput["notes"]>("incremental");
  const open = !!mode && !!session;
  const notesOnly = mode === "notes";

  useEffect(() => {
    if (!open) return;
    setAsrModel(session?.asrModel ?? "");
    setNotes(notesOnly ? "rebuild" : "incremental");
  }, [open, notesOnly, session?.asrModel]);
  useEffect(() => {
    if (open && !dialog.current?.open) dialog.current?.showModal();
    else if (!open && dialog.current?.open) dialog.current.close();
  }, [open]);

  const running = session?.rerun?.status === "running";
  const minutes = session ? rerunAudioMinutes(session) : 0;
  const requests = Math.max(1, Math.ceil((minutes * 60) / windowSeconds));
  const model = asrModel || health?.asr?.model || "the default model";
  const blocked = recording
    ? "Stop recording before rerunning this session."
    : running
      ? "A rerun is already running for this session."
      : !notesOnly && minutes === 0
        ? "This session has no saved speech to re-transcribe."
        : "";

  return (
    <Dialog
      ref={dialog}
      onCancel={(event) => {
        event.preventDefault();
        setMode(null);
      }}
      aria-labelledby="rerun-dialog-title"
    >
      <form
        className="grid gap-3 p-5.5"
        onSubmit={(event) => {
          event.preventDefault();
          if (!session || blocked) return;
          void startRerun(session, {
            asr: !notesOnly,
            asrModel: notesOnly ? null : asrModel || null,
            windowSeconds,
            notes,
          }).then(() => {
            if (!usePlaybackStore.getState().error) setMode(null);
          });
        }}
      >
        <h2 id="rerun-dialog-title" className="mb-1 text-[17px] font-[750]">
          {notesOnly ? "Rebuild notes" : "Rerun session"}
        </h2>
        {!notesOnly && (
          <>
            <p className={noteStyle}>
              The saved audio is recognized again in passages of about{" "}
              {windowSeconds} seconds instead of 8-second pieces, so the model
              hears whole sentences. Each passage replaces the rows it covers;
              their first-pass text stays in the row details.
            </p>
            {!!health?.asrModels?.length && (
              <>
                <label className={labelStyle} htmlFor="rerun-model">
                  Transcription model
                </label>
                <select
                  id="rerun-model"
                  className={selectStyle}
                  value={asrModel}
                  onChange={(event) => setAsrModel(event.target.value)}
                >
                  <option value="">Default · {health.asr?.model}</option>
                  {health.asrModels.map((item) => (
                    <option key={item} value={item}>
                      {item}
                    </option>
                  ))}
                </select>
              </>
            )}
            <label className={labelStyle} htmlFor="rerun-window">
              Passage length
            </label>
            <select
              id="rerun-window"
              className={selectStyle}
              value={windowSeconds}
              onChange={(event) => setWindowSeconds(Number(event.target.value))}
            >
              {windowChoices.map((seconds) => (
                <option key={seconds} value={seconds}>
                  About {seconds} seconds (cut at a pause)
                </option>
              ))}
            </select>
            <p className={noteStyle} data-testid="rerun-estimate">
              Uploads about {minutes.toFixed(1)} minutes of audio in about{" "}
              {requests} paid requests to {model}.
            </p>
            <span className={labelStyle} id="rerun-notes-label">
              Notes afterwards
            </span>
            <div role="group" aria-labelledby="rerun-notes-label">
              <Segmented>
                {noteModes.map((item) => (
                  <Segment
                    key={item.value}
                    type="button"
                    pressed={notes === item.value}
                    onClick={() => setNotes(item.value)}
                  >
                    {item.label}
                  </Segment>
                ))}
              </Segmented>
            </div>
          </>
        )}
        <p className={noteStyle}>
          {noteModes.find((item) => item.value === notes)?.note}
        </p>
        {blocked && (
          <p className="text-[12px] text-[#a13232]" role="alert">
            {blocked}
          </p>
        )}
        <DialogActions>
          <DialogButton type="button" onClick={() => setMode(null)}>
            Cancel
          </DialogButton>
          <DialogButton
            variant="primary"
            type="submit"
            disabled={!!blocked || busy === "rerun"}
          >
            {busy === "rerun"
              ? "Starting…"
              : notesOnly
                ? "Rebuild notes"
                : "Start rerun"}
          </DialogButton>
        </DialogActions>
      </form>
    </Dialog>
  );
}
