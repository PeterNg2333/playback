import { useEffect, useRef } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { NOTE_MATERIAL_CHARS, type MaterialBudget } from "./attachMaterial";

const count = (n: number) => n.toLocaleString("en");

// Shown after adding a material when the session's materials run past what the note model reads, so the owner sees how
// often that happens and which files are cut.
export function MaterialBudgetDialog({
  budget,
  onClose,
}: {
  budget: MaterialBudget;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  return (
    <Dialog
      ref={dialog}
      aria-labelledby="material-budget-title"
      onClose={onClose}
    >
      <div className="grid gap-2.5 p-5 text-[12px] leading-[1.5]">
        <h2 id="material-budget-title" className="text-base font-bold">
          Materials are longer than the notes read
        </h2>
        <p>
          "{budget.added}" was added. This session's materials now hold{" "}
          <strong>{count(budget.total)}</strong> characters, but the AI reads
          only the first <strong>{count(NOTE_MATERIAL_CHARS)}</strong> when it
          writes notes and minutes, in the order the files were added.
        </p>
        <ul className="list-disc pl-5">
          {budget.partly && (
            <li>
              Read in part: <strong>{budget.partly}</strong>
            </li>
          )}
          {budget.unread.length > 0 && (
            <li>
              Not read for notes: <strong>{budget.unread.join(", ")}</strong>
            </li>
          )}
        </ul>
        <p className="text-muted">
          Ask still searches every file, and names and terms from every file
          still help speech recognition. For the notes, put the most useful
          material first, such as attendees, agenda and open action items.
        </p>
        <DialogActions>
          <DialogButton variant="primary" onClick={onClose} autoFocus>
            OK
          </DialogButton>
        </DialogActions>
      </div>
    </Dialog>
  );
}
