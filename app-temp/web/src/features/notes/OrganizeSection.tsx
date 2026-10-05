import { useEffect, useRef, useState } from "react";
import { api } from "../../lib/backend/client";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { Button } from "../../components/Button";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";

// Asks the AI to reorganise one saved notes section. Leaving the session cancels the request.
export function OrganizeSection({
  session,
  draftDirty,
  onClose,
}: {
  session: Session;
  draftDirty: boolean;
  onClose: () => void;
}) {
  const [sectionId, setSectionId] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const request = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => request.current?.abort(), []);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  const sections = session.currentNote?.sections ?? [];

  async function organize() {
    const controller = new AbortController();
    request.current = controller;
    setPending(true);
    setError("");
    try {
      await api.post(
        `/sessions/${session.id}/notes/organize`,
        { sectionId, basedOnVersion: session.noteVersion },
        undefined,
        { signal: controller.signal },
      );
      if (!controller.signal.aborted) {
        await refreshWorkspace(session.id);
        onClose();
      }
    } catch (e) {
      if (!controller.signal.aborted)
        setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (!controller.signal.aborted) setPending(false);
    }
  }

  if (!sections.length) return null;
  return (
    <Dialog
      ref={dialog}
      aria-labelledby="organize-title"
      onCancel={(event) => {
        event.preventDefault();
        if (!pending) onClose();
      }}
    >
      <div className="grid gap-3 p-5">
        <h2 id="organize-title" className="text-[17px] font-bold">
          Organize a section
        </h2>
        <label className="text-[12px] text-muted" htmlFor="organize-section">
          Section to organize
        </label>
        <select
          className="w-full min-w-0 rounded-lg border border-line bg-white p-2 text-[13px]"
          id="organize-section"
          value={sectionId}
          onChange={(e) => setSectionId(e.target.value)}
        >
          <option value="">Select section…</option>
          {sections.map((x) => (
            <option key={x.id} value={x.id}>
              {x.title}
              {x.userEdited ? " (user edited)" : ""}
            </option>
          ))}
        </select>
        {draftDirty && (
          <p className="text-[12px] text-muted">
            Save your edits before organizing.
          </p>
        )}
        <DialogActions>
          <DialogButton disabled={pending} onClick={onClose}>
            Cancel
          </DialogButton>
          <Button
            disabled={!sectionId || pending || draftDirty}
            onClick={() => void organize()}
          >
            {pending ? "Organizing…" : "Organize section"}
          </Button>
        </DialogActions>
        {error && <p role="alert">{error}</p>}
      </div>
    </Dialog>
  );
}
