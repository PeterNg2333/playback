import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { api } from "../../lib/backend/client";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";

type Block = NonNullable<NonNullable<Session["currentNote"]>["sections"]>[number]["blocks"];
export function NoteBlockEditor({session, block}: {session: Session; block: NonNullable<Block>[number]}) {
  const [open, setOpen] = useState(false);
  return <><button className="text-[11px] text-accent underline" onClick={() => setOpen(true)}>Edit {block.role === "legacy" ? "text" : block.role}</button>
    {open && <Editor session={session} block={block} close={() => setOpen(false)} />}</>;
}
function Editor({session, block, close}: {session: Session; block: NonNullable<Block>[number]; close: () => void}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [baseVersion] = useState(session.noteVersion);
  const [body, setBody] = useState(block.body);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => { dialog.current?.showModal(); }, []);
  async function save() {
    setSaving(true); setError("");
    try { await api.post(`/sessions/${session.id}/notes/block`, {basedOnVersion: baseVersion, blockId: block.id, body}); await refreshWorkspace(session.id); close(); }
    catch (failure) { setError(failure instanceof Error ? failure.message : String(failure)); }
    finally { setSaving(false); }
  }
  return <Dialog ref={dialog} aria-labelledby="block-editor-title" onClose={close} onCancel={event => {if (saving) event.preventDefault();}}>
    <div className="grid gap-3 p-5"><h2 id="block-editor-title" className="font-bold">Edit note text</h2>
      <textarea aria-label="Note text" className="min-h-52 w-full rounded border border-line p-2 font-mono text-[12px]" value={body} disabled={saving} onChange={event => setBody(event.target.value)} />
      <p className="text-[12px] text-muted">Saved text is protected from automatic rewriting.</p>
      {error && <p role="alert" className="text-[12px] text-red-700">{error} Your draft is retained.</p>}
      <DialogActions><DialogButton disabled={saving} onClick={close}>Cancel</DialogButton><DialogButton disabled={saving || !body.trim()} onClick={() => void save()}>Save text</DialogButton></DialogActions>
    </div></Dialog>;
}
