import { useEffect, useRef, useState } from "react";
import { z } from "zod";
import { api } from "../../pages/api";
import { Markdown } from "../../Component/Markdown";
import { useHealth } from "../../lib/useHealth";
import { refreshWorkspace } from "../library/refreshWorkspace";
import type { Session } from "../../types/api";
import { restoreNoteVersion } from "./restoreNoteVersion";

const HistorySchema = z.object({
  items: z.array(
    z.object({
      version: z.number(),
      author: z.string(),
      createdAt: z.string(),
    }),
  ),
  nextBefore: z.number().nullable(),
});
const RecoverySchema = z.object({
  basedOnVersion: z.number(),
  historicalVersion: z.number(),
  citations: z.array(
    z.object({ id: z.string(), sourceIds: z.array(z.string()) }),
  ),
  candidates: z.array(
    z.object({
      id: z.string(),
      title: z.string(),
      markdown: z.string(),
      missingSourceIds: z.array(z.string()),
      blocked: z.boolean(),
      userEdited: z.boolean(),
    }),
  ),
});
type Recovery = z.infer<typeof RecoverySchema>;

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
  const [items, setItems] = useState<z.infer<typeof HistorySchema>["items"]>(
    [],
  );
  const [before, setBefore] = useState<number | null>(null);
  const [version, setVersion] = useState(1);
  const [historical, setHistorical] = useState("");
  const [preview, setPreview] = useState<Recovery>();
  const [selected, setSelected] = useState<string[]>([]);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const dialog = useRef<HTMLDialogElement>(null);
  const request = useRef<AbortController | undefined>(undefined);
  function closeHistory() {
    request.current?.abort();
    request.current = undefined;
    setOpen(false);
    setItems([]);
    setBefore(null);
    setHistorical("");
    setPreview(undefined);
    setSelected([]);
    setPending(false);
    setError("");
  }
  useEffect(() => {
    closeHistory();
  }, [session?.id]);
  useEffect(() => {
    if (open) dialog.current?.showModal();
    else dialog.current?.close();
  }, [open]);
  useEffect(() => () => request.current?.abort(), []);
  async function load(work: (signal: AbortSignal) => Promise<void>) {
    if (!session) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setPending(true);
    setError("");
    try {
      await work(controller.signal);
    } catch (e) {
      if (!controller.signal.aborted)
        setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (request.current === controller) setPending(false);
    }
  }
  async function history(cursor?: number) {
    await load(async (signal) => {
      const page = await api(
        `/sessions/${session!.id}/notes/history${cursor ? "?before=" + cursor : ""}`,
        "GET",
        undefined,
        HistorySchema,
        signal,
      );
      if (signal.aborted) return;
      setItems(page.items);
      setBefore(page.nextBefore);
    });
  }
  return (
    <>
      <button
        className="text-control"
        disabled={!session || !health?.sectionNotes}
        onClick={() => {
          setOpen(true);
          void history();
        }}
      >
        History & recovery
      </button>
      <dialog
        ref={dialog}
        className="notes-history-dialog"
        aria-labelledby="note-history-title"
        onCancel={closeHistory}
      >
        {open && (
          <>
            <header>
              <h2 id="note-history-title">Note history & recovery</h2>
              <button aria-label="Close note history" onClick={closeHistory}>
                ×
              </button>
            </header>
            <p>
              Review historical sections before adding them to the current
              version. Protected deletions are excluded. Existing notes and
              editor text are retained.
            </p>
            {pending && <p role="status">Loading…</p>}
            {error && <p role="alert">{error}</p>}
            <div className="history-layout">
              <nav aria-label="All note versions">
                {items.map((x) => (
                  <button
                    key={x.version}
                    onClick={() => {
                      setVersion(x.version);
                      setPreview(undefined);
                      void load(async (signal) => {
                        const note = await api<{ markdown: string }>(
                          `/sessions/${session!.id}/notes/${x.version}`,
                          "GET",
                          undefined,
                          undefined,
                          signal,
                        );
                        if (!signal.aborted) setHistorical(note.markdown);
                      });
                    }}
                  >
                    v{x.version} · {x.author}
                  </button>
                ))}
                {before && (
                  <button disabled={pending} onClick={() => history(before)}>
                    Load older versions
                  </button>
                )}
                <label>
                  Go to version
                  <input
                    type="number"
                    min={1}
                    value={version}
                    onChange={(e) => setVersion(Number(e.target.value))}
                  />
                </label>
                <button
                  disabled={pending || version < 1}
                  onClick={() =>
                    load(async (signal) => {
                      const note = await api<{ markdown: string }>(
                        `/sessions/${session!.id}/notes/${version}`,
                        "GET",
                        undefined,
                        undefined,
                        signal,
                      );
                      if (!signal.aborted) {
                        setHistorical(note.markdown);
                        setPreview(undefined);
                      }
                    })
                  }
                >
                  Read version
                </button>
              </nav>
              <main>
                {historical && (
                  <>
                    <h3>Saved v{version}</h3>
                    <pre className="historical-note">{historical}</pre>
                    <button
                      disabled={pending || draftDirty}
                      onClick={() =>
                        load(async (signal) => {
                          await restoreNoteVersion(
                            session!.id,
                            version,
                            session!.noteVersion,
                            signal,
                          );
                          if (!signal.aborted) {
                            await refreshWorkspace(session!.id);
                            closeHistory();
                          }
                        })
                      }
                    >
                      Restore this version as a new protected version
                    </button>
                    <button
                      disabled={pending}
                      onClick={() =>
                        load(async (signal) => {
                          const result = await api(
                            `/sessions/${session!.id}/notes/${version}/recovery`,
                            "GET",
                            undefined,
                            RecoverySchema,
                            signal,
                          );
                          if (!signal.aborted) {
                            setPreview(result);
                            setSelected([]);
                          }
                        })
                      }
                    >
                      Preview recovery into current notes
                    </button>
                  </>
                )}
                {preview && (
                  <section aria-label="Recovery preview">
                    <h3>
                      Merge v{preview.historicalVersion} into v
                      {preview.basedOnVersion}
                    </h3>
                    <p>
                      Historical wording is preserved for review. Check its
                      claims against the original ASR before saving; source
                      membership alone does not validate meaning. Unavailable
                      historical references remain unavailable.
                    </p>
                    {!preview.candidates.length && (
                      <p>
                        No missing source-supported section was found. Source
                        membership alone cannot prove complete semantic
                        coverage.
                      </p>
                    )}
                    {preview.candidates.map((candidate) => (
                      <article key={candidate.id}>
                        <label>
                          <input
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
                          <p>Excluded: overlaps an intentional deletion.</p>
                        )}
                        <Markdown
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
                      disabled={!selected.length || pending || draftDirty}
                      onClick={() =>
                        load(async (signal) => {
                          await api(
                            `/sessions/${session!.id}/notes/${preview.historicalVersion}/recovery`,
                            "POST",
                            {
                              basedOnVersion: preview.basedOnVersion,
                              selectedIds: selected,
                            },
                            undefined,
                            signal,
                          );
                          if (!signal.aborted) {
                            await refreshWorkspace(session!.id);
                            closeHistory();
                          }
                        })
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
