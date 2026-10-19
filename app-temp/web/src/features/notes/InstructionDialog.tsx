import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { api } from "../../lib/backend/client";
import {
  InstructionResultSchema,
  type Session,
} from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";

// Tell the AI how to change the notes: for all unlocked notes, one topic, or one block (a comment on it).
// The change applies directly; history keeps the previous version, and locked blocks are never touched.
export function InstructionDialog({
  session,
  blockId,
  onClose,
}: {
  session: Session;
  blockId?: string;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [baseVersion] = useState(session.noteVersion);
  const [topicId, setTopicId] = useState("");
  const [instruction, setInstruction] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [outcome, setOutcome] = useState("");
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  async function send() {
    setBusy(true);
    setError("");
    setOutcome("");
    try {
      const result = await api.post(
        `/sessions/${session.id}/notes/instruct`,
        {
          basedOnVersion: baseVersion,
          instruction,
          topicId: blockId ? null : topicId || null,
          blockId: blockId ?? null,
        },
        InstructionResultSchema,
      );
      const summary =
        result.applied > 0
          ? `${result.applied} changes applied.${result.notApplied.length ? ` Not applied: ${result.notApplied.join("; ")}` : ""}`
          : `Nothing was changed${result.notApplied.length ? `: ${result.notApplied.join("; ")}` : "."}`;
      // The instruction is saved at this point; a failed reload must not read as a failed instruction.
      try {
        await refreshWorkspace(session.id);
      } catch (failure) {
        setOutcome(
          `${summary} Reloading the notes failed: ${failure instanceof Error ? failure.message : String(failure)}`,
        );
        return;
      }
      if (result.applied > 0 && result.notApplied.length === 0) {
        onClose();
        return;
      }
      setOutcome(summary);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Dialog
      ref={dialog}
      aria-labelledby="instruction-title"
      onClose={onClose}
      onCancel={(event) => {
        if (busy) event.preventDefault();
      }}
    >
      <div className="grid gap-3 p-5 text-[12px]">
        <h2 id="instruction-title" className="text-base font-bold">
          {blockId
            ? "Comment to AI on this text"
            : "Revise with an instruction"}
        </h2>
        {!blockId && (
          <label>
            Apply to
            <select
              aria-label="Instruction target"
              className="mt-1 w-full rounded border border-line p-2"
              value={topicId}
              disabled={busy}
              onChange={(event) => setTopicId(event.target.value)}
            >
              <option value="">All unlocked notes</option>
              {session.currentNote?.sections?.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.title}
                </option>
              ))}
            </select>
          </label>
        )}
        <label>
          Instruction
          <textarea
            aria-label="Instruction"
            className="mt-1 min-h-28 w-full rounded border border-line p-2"
            maxLength={2000}
            value={instruction}
            disabled={busy}
            placeholder="e.g. Turn this into a comparison table; the command is NtQueryFile"
            onChange={(event) => setInstruction(event.target.value)}
          />
        </label>
        <p className="text-muted">
          The AI changes the notes directly. Earlier versions stay in history;
          locked text is never changed.
        </p>
        {outcome && <p role="status">{outcome}</p>}
        {error && (
          <p role="alert" className="text-red-700">
            {error} Your instruction is retained.
          </p>
        )}
        <DialogActions>
          <DialogButton disabled={busy} onClick={onClose}>
            Close
          </DialogButton>
          <DialogButton
            disabled={busy || !instruction.trim()}
            onClick={() => void send()}
          >
            {busy ? "Applying…" : "Apply instruction"}
          </DialogButton>
        </DialogActions>
      </div>
    </Dialog>
  );
}
