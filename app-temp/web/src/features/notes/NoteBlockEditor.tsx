import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { api } from "../../lib/backend/client";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { runAction } from "../../lib/store";
import { InstructionDialog } from "./InstructionDialog";

type Block = NonNullable<
  NonNullable<Session["currentNote"]>["sections"]
>[number]["blocks"];
// Per block: edit it yourself, lock it so AI never changes it (userEdited is the lock), or comment to the AI.
export function NoteBlockEditor({
  session,
  block,
}: {
  session: Session;
  block: NonNullable<Block>[number];
}) {
  const [open, setOpen] = useState<"edit" | "comment" | null>(null);
  const [locking, setLocking] = useState(false);
  const label = block.role === "legacy" ? "text" : block.role;
  async function toggleLock() {
    setLocking(true);
    // runAction shows a failed lock (for example, notes changed meanwhile) as the page error.
    await runAction(
      block.userEdited ? "Unlocking text" : "Locking text",
      async () => {
        await api.post(`/sessions/${session.id}/notes/block/lock`, {
          basedOnVersion: session.noteVersion,
          blockId: block.id,
          locked: !block.userEdited,
        });
        await refreshWorkspace(session.id);
      },
    );
    setLocking(false);
  }
  return (
    <span className="inline-flex gap-2 text-[11px]">
      <button className="text-accent underline" onClick={() => setOpen("edit")}>
        Edit {label}
      </button>
      <button
        className="text-accent underline"
        disabled={locking}
        aria-pressed={block.userEdited}
        onClick={() => void toggleLock()}
      >
        {block.userEdited ? `Unlock ${label}` : `Lock ${label}`}
      </button>
      <button
        className="text-accent underline"
        disabled={block.userEdited}
        title={
          block.userEdited
            ? "Unlock this text before asking the AI to change it"
            : undefined
        }
        onClick={() => setOpen("comment")}
      >
        Comment to AI
      </button>
      {open === "edit" && (
        <Editor session={session} block={block} close={() => setOpen(null)} />
      )}
      {open === "comment" && (
        <InstructionDialog
          session={session}
          blockId={block.id}
          onClose={() => setOpen(null)}
        />
      )}
    </span>
  );
}
function Editor({
  session,
  block,
  close,
}: {
  session: Session;
  block: NonNullable<Block>[number];
  close: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [baseVersion] = useState(session.noteVersion);
  const [body, setBody] = useState(block.body);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  async function save() {
    setSaving(true);
    setError("");
    try {
      await api.post(`/sessions/${session.id}/notes/block`, {
        basedOnVersion: baseVersion,
        blockId: block.id,
        body,
      });
      await refreshWorkspace(session.id);
      close();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setSaving(false);
    }
  }
  return (
    <Dialog
      ref={dialog}
      aria-labelledby="block-editor-title"
      onClose={close}
      onCancel={(event) => {
        if (saving) event.preventDefault();
      }}
    >
      <div className="grid gap-3 p-5">
        <h2 id="block-editor-title" className="font-bold">
          Edit note text
        </h2>
        <textarea
          aria-label="Note text"
          className="min-h-52 w-full rounded border border-line p-2 font-mono text-[12px]"
          value={body}
          disabled={saving}
          onChange={(event) => setBody(event.target.value)}
        />
        <p className="text-[12px] text-muted">
          Saved text is protected from automatic rewriting.
        </p>
        {error && (
          <p role="alert" className="text-[12px] text-red-700">
            {error} Your draft is retained.
          </p>
        )}
        <DialogActions>
          <DialogButton disabled={saving} onClick={close}>
            Cancel
          </DialogButton>
          <DialogButton
            disabled={saving || !body.trim()}
            onClick={() => void save()}
          >
            Save text
          </DialogButton>
        </DialogActions>
      </div>
    </Dialog>
  );
}
