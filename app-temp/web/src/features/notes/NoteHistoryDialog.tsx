import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { api } from "../../lib/backend/client";
import { CitedMarkdown } from "../sources/CitedMarkdown";
import { useHealth } from "../../lib/useHealth";
import { refreshWorkspace } from "../library/refreshWorkspace";
import {
  NoteHistoryPageSchema,
  NoteRecoverySchema,
  SavedNoteSchema,
  type Session,
} from "../../lib/backend/schemas";
import { restoreNoteVersion } from "./restoreNoteVersion";
import { Button } from "../../components/Button";

// The dialog's buttons and the version field: hairline boxes on the surface colour.
const control =
  "max-w-full rounded-md border border-line bg-surface px-2.5 py-1.5 text-ink disabled:text-muted disabled:opacity-65";
const heading = "mb-[1em] text-[1.17em] font-bold";
const paragraph = "mb-4";

// Browses saved note versions; restores one, or recovers chosen sections of it into the current notes.
export function NoteHistoryDialog({
  session,
  draftDirty,
}: {
  session: Session | null;
  draftDirty: boolean;
}) {
  const health = useHealth();
  const [open, setOpen] = useState(false);
  const [olderThan, setOlderThan] = useState<number>();
  const [typedVersion, setTypedVersion] = useState(1);
  const [shownVersion, setShownVersion] = useState<number>();
  const [recoveryVersion, setRecoveryVersion] = useState<number>();
  const [selected, setSelected] = useState<string[]>([]);
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
  const recovery = useQuery({
    queryKey: ["noteRecovery", id, recoveryVersion],
    queryFn: ({ signal }) =>
      api.get(
        `/sessions/${id}/notes/${recoveryVersion}/recovery`,
        NoteRecoverySchema,
        { signal },
      ),
    enabled: open && !!id && recoveryVersion !== undefined,
  });
  const items = page.data?.items ?? [];
  const before = page.data?.nextBefore;
  const historical = shown.data?.markdown;
  const preview = recovery.data;
  const pending =
    saving || page.isFetching || shown.isFetching || recovery.isFetching;
  const error =
    saveError || (page.error ?? shown.error ?? recovery.error)?.message;

  function closeHistory() {
    write.current?.abort();
    setOpen(false);
    setOlderThan(undefined);
    setShownVersion(undefined);
    setRecoveryVersion(undefined);
    setSelected([]);
    setSaving(false);
    setSaveError("");
  }
  useEffect(() => {
    if (open) dialog.current?.showModal();
    else dialog.current?.close();
  }, [open]);
  useEffect(() => () => write.current?.abort(), []);

  function show(version: number) {
    setShownVersion(version);
    setRecoveryVersion(undefined);
  }

  // Saves a new version from history, then shows the current notes.
  async function saveFromHistory(
    work: (signal: AbortSignal) => Promise<unknown>,
  ) {
    const controller = new AbortController();
    write.current = controller;
    setSaving(true);
    setSaveError("");
    try {
      await work(controller.signal);
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
    <>
      <Button
        disabled={!session || !health?.sectionNotes}
        onClick={() => setOpen(true)}
      >
        History & recovery
      </Button>
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
                Note history & recovery
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
              Review historical sections before adding them to the current
              version. Protected deletions are excluded. Existing notes and
              editor text are retained.
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
                      show(x.version);
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
                  onClick={() => show(typedVersion)}
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
                      onClick={() =>
                        saveFromHistory((signal) =>
                          restoreNoteVersion(
                            session!.id,
                            shownVersion,
                            session!.noteVersion,
                            signal,
                          ),
                        )
                      }
                    >
                      Restore this version as a new protected version
                    </button>
                    <button
                      className={control}
                      disabled={pending}
                      onClick={() => {
                        setRecoveryVersion(shownVersion);
                        setSelected([]);
                      }}
                    >
                      Preview recovery into current notes
                    </button>
                  </>
                )}
                {preview && (
                  <section aria-label="Recovery preview">
                    <h3 className={heading}>
                      Merge v{preview.historicalVersion} into v
                      {preview.basedOnVersion}
                    </h3>
                    <p className={paragraph}>
                      Historical wording is preserved for review. Check its
                      claims against the original ASR before saving; source
                      membership alone does not validate meaning. Unavailable
                      historical references remain unavailable.
                    </p>
                    {!preview.candidates.length && (
                      <p className={paragraph}>
                        No missing source-supported section was found. Source
                        membership alone cannot prove complete semantic
                        coverage.
                      </p>
                    )}
                    {preview.candidates.map((candidate) => (
                      <article
                        className="my-2.5 rounded-lg border border-line p-3"
                        key={candidate.id}
                      >
                        <label>
                          <input
                            className="my-0.75 mr-0.75 ml-1"
                            type="checkbox"
                            disabled={candidate.blocked}
                            checked={selected.includes(candidate.id)}
                            onChange={(e) =>
                              setSelected((old) =>
                                e.target.checked
                                  ? [...old, candidate.id]
                                  : old.filter((x) => x !== candidate.id),
                              )
                            }
                          />
                          {candidate.title} ·{" "}
                          {candidate.missingSourceIds.length} missing sources
                        </label>
                        {candidate.blocked && (
                          <p className={paragraph}>
                            Excluded: overlaps an intentional deletion.
                          </p>
                        )}
                        <CitedMarkdown
                          value={candidate.markdown}
                          reading
                          groups={preview.citations.map((x) => ({
                            id: x.id,
                            transcriptIds: x.sourceIds,
                          }))}
                        />
                      </article>
                    ))}
                    <button
                      className={control}
                      disabled={!selected.length || pending || draftDirty}
                      onClick={() =>
                        saveFromHistory((signal) =>
                          api.post(
                            `/sessions/${session!.id}/notes/${preview.historicalVersion}/recovery`,
                            {
                              basedOnVersion: preview.basedOnVersion,
                              selectedIds: selected,
                            },
                            undefined,
                            { signal },
                          ),
                        )
                      }
                    >
                      Save selected recovery as a new version
                    </button>
                  </section>
                )}
              </main>
            </div>
          </>
        )}
      </dialog>
    </>
  );
}
