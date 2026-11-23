import clsx from "clsx";
import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { NoteLanguageSchema, type Session } from "../../lib/backend/schemas";

// Asked before every recording: what the notes are written as (meeting minutes or lecture notes) and in which language,
// since the notes are written while recording. The current settings are chosen; Start keeps or changes them.
export type RecordChoice = {
  kind: Session["noteKind"];
  language: Session["noteLanguage"];
};

const kinds: {
  value: Session["noteKind"];
  label: string;
  note: string;
}[] = [
  {
    value: "meeting",
    label: "Meeting minutes",
    note: "Summary, attendees, decisions, questions and clarifications, action items with owners and deadlines.",
  },
  {
    value: "lecture",
    label: "Lecture notes",
    note: "Study notes by topic, key concepts, a summary and review questions.",
  },
];

export function RecordStartDialog({
  session,
  onStart,
  onCancel,
}: {
  session: Session;
  onStart: (choice: RecordChoice) => void;
  onCancel: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [kind, setKind] = useState(session.noteKind);
  const [language, setLanguage] = useState(session.noteLanguage);
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  return (
    <Dialog
      ref={dialog}
      className="w-[min(460px,calc(100vw-32px))]"
      aria-labelledby="record-start-title"
      onClose={onCancel}
    >
      <form
        className="grid gap-3 p-5 text-[12px]"
        onSubmit={(event) => {
          event.preventDefault();
          onStart({ kind, language });
        }}
      >
        <h2 id="record-start-title" className="text-base font-bold">
          What should the notes be?
        </h2>
        <div className="grid gap-2" role="radiogroup" aria-label="Note type">
          {kinds.map((option) => (
            <label
              key={option.value}
              className={clsx(
                "grid cursor-pointer gap-0.5 rounded-lg border p-3",
                kind === option.value
                  ? "border-accent bg-accent-soft"
                  : "border-line bg-white",
              )}
            >
              <span className="flex items-center gap-2 text-[13px] font-bold">
                <input
                  type="radio"
                  name="note-kind"
                  value={option.value}
                  checked={kind === option.value}
                  onChange={() => setKind(option.value)}
                />
                {option.label}
              </span>
              <span className="pl-5.5 text-[11px] text-muted">
                {option.note}
              </span>
            </label>
          ))}
        </div>
        <label className="grid gap-1 text-[12px] font-bold">
          Notes language
          <select
            className="w-full rounded-lg border border-line bg-white p-2 text-[13px] font-normal text-ink"
            value={language}
            onChange={(event) =>
              setLanguage(NoteLanguageSchema.parse(event.target.value))
            }
          >
            <option value="zh-Hant">TC · Traditional Chinese</option>
            <option value="zh-Hans">SC · Simplified Chinese</option>
            <option value="en">EN · English</option>
          </select>
        </label>
        <p className="text-[11px] text-muted">
          Both can be changed later from the notes menu, which rewrites the
          notes.
        </p>
        <DialogActions>
          <DialogButton type="button" onClick={onCancel}>
            Cancel
          </DialogButton>
          <DialogButton type="submit" variant="primary" autoFocus>
            Start recording
          </DialogButton>
        </DialogActions>
      </form>
    </Dialog>
  );
}
