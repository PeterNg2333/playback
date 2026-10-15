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
  const [baseVersion] = useState(session.noteVersion);
  const [manual, setManual] = useState(false);
  const [parentId, setParentId] = useState("");
  const [order, setOrder] = useState(0);
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
        `/sessions/${session.id}/notes/${manual ? "structure" : "organize"}`,
        manual ? { basedOnVersion: baseVersion, operations: [{kind: "move-topic", topicId: sectionId, parentId: parentId || null, order}] } : { sectionId, basedOnVersion: baseVersion },
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
        <label className="flex gap-2 text-[12px]"><input type="checkbox" checked={manual} disabled={pending} onChange={event => setManual(event.target.checked)} />Move topic in the outline</label>
        {manual && <>
          <label className="text-[12px]">Parent topic<select aria-label="Parent topic" className="mt-1 w-full rounded border border-line p-2" value={parentId} onChange={event => setParentId(event.target.value)}><option value="">Top level</option>{sections.filter(x => x.id !== sectionId).map(x => <option key={x.id} value={x.id}>{x.title}</option>)}</select></label>
          <label className="text-[12px]">Sibling order<input aria-label="Sibling order" className="ml-2 w-20 rounded border border-line p-2" type="number" min={0} max={10000} value={order} onChange={event => setOrder(Number(event.target.value))} /></label>
          <p className="text-[12px] text-muted">Bodies and citations stay intact. This selected action may move protected content.</p>
        </>}
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
            {pending ? "Saving…" : manual ? "Move topic" : "Organize section"}
          </Button>
        </DialogActions>
        {error && <p role="alert">{error}</p>}
      </div>
    </Dialog>
  );
}
