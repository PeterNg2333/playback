import {useEffect, useRef, useState} from "react";
import {useQuery} from "@tanstack/react-query";
import {Dialog, DialogActions, DialogButton} from "../../components/Dialog";
import {api} from "../../lib/backend/client";
import {NoteRewriteSchema, type Evidence, type Session} from "../../lib/backend/schemas";
import type {AudioPlayer} from "../player/useAudioPlayer";
import {NotePreview} from "./NotePreview";
import {refreshWorkspace} from "../library/refreshWorkspace";

export function RewriteDialog({session, draftDirty, onClose, onOpenSource, onPlay}: {session: Session; draftDirty: boolean; onClose: () => void; onOpenSource: (source: Evidence) => void; onPlay: AudioPlayer["togglePlayback"]}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const request = useRef<AbortController | undefined>(undefined);
  const [sectionId, setSectionId] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [semanticAccepted, setSemanticAccepted] = useState(false);
  const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const path = `/sessions/${session.id}/notes/rewrites`;
  const query = useQuery({queryKey: ["noteRewrites", session.id, session.noteVersion], queryFn: ({signal}) => api.get(path, NoteRewriteSchema.array(), {signal})});
  const selected = query.data?.find(x => x.id === selectedId) ?? query.data?.[0];
  useEffect(() => {dialog.current?.showModal(); return () => request.current?.abort();}, []);
  async function perform(preview: boolean) {
    const controller = new AbortController(); request.current = controller; setBusy(true); setError("");
    try {
      if (preview) {
        const proposal = await api.post(path + "/preview", {sectionId, basedOnVersion: session.noteVersion}, NoteRewriteSchema, {signal: controller.signal});
        setSelectedId(proposal.id); setSemanticAccepted(false); await query.refetch();
      } else if (selected) {
        await api.post(path + `/${selected.id}/accept`, {semanticAccepted}, undefined, {signal: controller.signal});
        await refreshWorkspace(session.id); onClose();
      }
    } catch (failure) {if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : String(failure));}
    finally {if (!controller.signal.aborted) setBusy(false);}
  }
  const render = (body: string) => <NotePreview session={{...session, noteMarkdown: body, currentNote: session.currentNote ? {...session.currentNote, sections: []} : null}} markdown={body} reading={false} onReference={() => {}} onOpenSource={onOpenSource} onPlay={onPlay} />;
  return <Dialog ref={dialog} aria-labelledby="rewrite-title" onClose={onClose} onCancel={event => {if (busy) event.preventDefault();}}>
    <div className="grid max-h-[85vh] gap-3 overflow-y-auto p-5 text-[12px]"><h2 id="rewrite-title" className="text-base font-bold">Review a rewrite</h2>
      <p>Original text stays in place until you review and accept a preview.</p>
      <label>Topic<select aria-label="Rewrite topic" className="mt-1 w-full rounded border border-line p-2" value={sectionId} onChange={event => setSectionId(event.target.value)}><option value="">Select topic…</option>{session.currentNote?.sections?.map(x => <option key={x.id} value={x.id}>{x.title}</option>)}</select></label>
      <DialogButton disabled={busy || draftDirty || !sectionId} onClick={() => void perform(true)}>Generate preview</DialogButton>
      {draftDirty && <p>Save your editor changes before previewing or applying.</p>}
      {query.error && <p role="alert">{query.error.message}</p>}
      {query.data && query.data.length > 1 && <select aria-label="Saved preview" value={selected?.id ?? ""} onChange={event => {setSelectedId(event.target.value); setSemanticAccepted(false);}}>{query.data.map(x => <option key={x.id} value={x.id}>Version {x.basedOnVersion} · {x.status}</option>)}</select>}
      {selected ? <>
        <p>{selected.readyForAcceptance ? "Mechanical retention checks passed. Review the meaning and source support." : "Retention checks blocked this preview. Original text is preserved."}</p>
        {selected.basedOnVersion !== session.noteVersion && <p role="alert">Notes changed since this preview. Generate a fresh preview to apply changes.</p>}
        {selected.issues.length > 0 && <ul className="list-disc pl-5">{selected.issues.map((issue, i) => <li key={i}>{issue.replace(/: (?:block|point)_[a-f0-9]+/, "")}</li>)}</ul>}
        <div className="grid gap-4 md:grid-cols-2"><section><h3 className="mb-2 font-bold">Before</h3>{render(Object.values(selected.originalBodies).join("\n\n"))}</section><section><h3 className="mb-2 font-bold">Proposed</h3>{render(selected.changes.map(x => x.body).join("\n\n"))}</section></div>
        <label className="flex items-start gap-2"><input type="checkbox" checked={semanticAccepted} onChange={event => setSemanticAccepted(event.target.checked)} disabled={!selected.readyForAcceptance || selected.basedOnVersion !== session.noteVersion || busy} />I reviewed the meaning, qualifications, examples and source support.</label>
      </> : <p>No saved rewrite preview.</p>}
      {error && <p role="alert">{error} The preview is retained.</p>}
      <DialogActions><DialogButton disabled={busy} onClick={onClose}>Close</DialogButton><DialogButton disabled={busy || draftDirty || !semanticAccepted || !selected?.readyForAcceptance || selected.basedOnVersion !== session.noteVersion} onClick={() => void perform(false)}>Accept rewrite</DialogButton></DialogActions>
    </div></Dialog>;
}
