import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { api } from "../../lib/backend/client";
import { refreshWorkspace } from "../library/refreshWorkspace";
import {
  NoteHistoryPageSchema,
  SavedNoteSchema,
  type Session,
} from "../../lib/backend/schemas";
import { restoreNoteVersion } from "./restoreNoteVersion";

// The dialog's buttons and the version field: hairline boxes on the surface colour.
const control =
  "max-w-full rounded-md border border-line bg-surface px-2.5 py-1.5 text-ink disabled:text-muted disabled:opacity-65";
const heading = "mb-[1em] text-[1.17em] font-bold";
const paragraph = "mb-4";

// Browses saved note versions and restores one as a new version.
export function NoteHistoryDialog({
  session,
  draftDirty,
  onClose,
}: {
  session: Session | null;
  draftDirty: boolean;
  onClose: () => void;
}) {
  const [open, setOpen] = useState(true);
  const [olderThan, setOlderThan] = useState<number>();
  const [typedVersion, setTypedVersion] = useState(1);
  const [shownVersion, setShownVersion] = useState<number>();
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState("");
  const dialog = useRef<HTMLDialogElement>(null);
  // A restore still running when the dialog closes must not reload the notes afterwards.
  const write = useRef<AbortController | undefined>(undefined);
  const id = session?.id;

  const page = useQuery({
    queryKey: ["noteHistory", id, olderThan],
    queryFn: ({ signal }) =>
      api.get(
        `/sessions/${id}/notes/history${olderThan ? "?before=" + olderThan : ""}`,
        NoteHistoryPageSchema,
        { signal },
      ),
    enabled: open && !!id,
  });
  const shown = useQuery({
    queryKey: ["noteVersion", id, shownVersion],
    queryFn: ({ signal }) =>
      api.get(`/sessions/${id}/notes/${shownVersion}`, SavedNoteSchema, {
        signal,
      }),
    enabled: open && !!id && shownVersion !== undefined,
  });
  const items = page.data?.items ?? [];
  const before = page.data?.nextBefore;
  const historical = shown.data?.markdown;
  const pending = saving || page.isFetching || shown.isFetching;
  const error = saveError || (page.error ?? shown.error)?.message;

  function closeHistory() {
    write.current?.abort();
    setOpen(false);
    onClose();
    setOlderThan(undefined);
    setShownVersion(undefined);
    setSaving(false);
    setSaveError("");
  }
  useEffect(() => {
    if (open) dialog.current?.showModal();
    else dialog.current?.close();
  }, [open]);
  useEffect(() => () => write.current?.abort(), []);

  // Restores the shown version as a new version, then shows the current notes.
  async function restore(version: number) {
    const controller = new AbortController();
    write.current = controller;
    setSaving(true);
    setSaveError("");
    try {
      await restoreNoteVersion(
        session!.id,
        version,
        session!.noteVersion,
        controller.signal,
      );
      if (controller.signal.aborted) return;
      await refreshWorkspace(session!.id);
      closeHistory();
    } catch (e) {
      if (!controller.signal.aborted)
        setSaveError(e instanceof Error ? e.message : String(e));
    } finally {
      if (!controller.signal.aborted) setSaving(false);
    }
  }

  return (
    <dialog
      ref={dialog}
      className="m-auto h-[90vh] max-h-[90vh] w-[calc(100vw-20px)] max-w-300 rounded-[14px] border border-line bg-surface p-3 text-ink shadow-[0_20px_60px_#25305c30] backdrop:bg-[#20233866] open:flex open:flex-col open:overflow-hidden md:h-[75vh] md:w-[70vw] md:p-5"
      aria-labelledby="note-history-title"
      onCancel={closeHistory}
    >
      {open && (
        <>
          <header className="flex shrink-0 justify-between gap-3">
            <h2 id="note-history-title" className="text-[15px] font-[750]">
              Note history
            </h2>
            <button
              className={control}
              aria-label="Close note history"
              onClick={closeHistory}
            >
              ×
            </button>
          </header>
          <p className={paragraph}>
            Every change is a saved version. Restoring one saves it again as the
            newest version, with its text locked against AI changes.
          </p>
          {pending && (
            <p className={paragraph} role="status">
              Loading…
            </p>
          )}
          {error && (
            <p className={paragraph} role="alert">
              {error}
            </p>
          )}
          <div className="grid min-h-0 grid-cols-1 gap-4.5 overflow-auto md:grid-cols-[minmax(140px,220px)_minmax(0,1fr)]">
            <nav
              className="flex flex-col gap-1.25"
              aria-label="All note versions"
            >
              {items.map((x) => (
                <button
                  className={control}
                  key={x.version}
                  onClick={() => {
                    setTypedVersion(x.version);
                    setShownVersion(x.version);
                  }}
                >
                  v{x.version} · {x.author}
                </button>
              ))}
              {before && (
                <button
                  className={control}
                  disabled={pending}
                  onClick={() => setOlderThan(before)}
                >
                  Load older versions
                </button>
              )}
              <label>
                Go to version
                <input
                  className={control}
                  type="number"
                  min={1}
                  value={typedVersion}
                  onChange={(e) => setTypedVersion(Number(e.target.value))}
                />
              </label>
              <button
                className={control}
                disabled={pending || typedVersion < 1}
                onClick={() => setShownVersion(typedVersion)}
              >
                Read version
              </button>
            </nav>
            <main className="min-w-0">
              {historical && shownVersion !== undefined && (
                <>
                  <h3 className={heading}>Saved v{shownVersion}</h3>
                  <pre className="my-3 max-h-[34vh] overflow-auto text-[12px] whitespace-pre-wrap wrap-anywhere font-[revert]">
                    {historical}
                  </pre>
                  <button
                    className={control}
                    disabled={pending || draftDirty}
                    onClick={() => restore(shownVersion)}
                  >
                    Restore this version as a new protected version
                  </button>
                </>
              )}
            </main>
          </div>
        </>
      )}
    </dialog>
  );
}
