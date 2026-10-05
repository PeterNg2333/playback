import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { queryClient } from "../../lib/queryClient";
import { api } from "../../lib/backend/client";
import { formatMinutes } from "../../lib/time";
import { useHealth } from "../../lib/useHealth";
import { refreshWorkspace } from "../library/refreshWorkspace";
import {
  NoteCoverageSchema,
  SavedNoteVersionSchema,
  type Activity,
  type Evidence,
  type Session,
} from "../../lib/backend/schemas";
import { usePlaybackStore } from "../../lib/store";
import { Button } from "../../components/Button";
import { Dialog } from "../../components/Dialog";
import { IconButton } from "../../components/IconButton";
import { audioSourceLabel } from "../recording/AudioSourceBadge";

// How many transcript parts the saved notes cite, the gaps left, and repairing unaddressed speech.
export function NoteCoveragePanel({
  session,
  draftDirty,
  onOpenSource,
  latestRevision,
}: {
  session: Session | null;
  draftDirty: boolean;
  onOpenSource: (source: Evidence) => void;
  latestRevision?: Activity;
}) {
  const health = useHealth();
  const busy = usePlaybackStore((state) => state.busy);
  const workspaceLoading = usePlaybackStore((state) => state.workspaceLoading);
  const coverage = useQuery({
    queryKey: [
      "noteCoverage",
      session?.id,
      session?.noteVersion,
      session?.transcripts.length,
    ],
    queryFn: ({ signal }) =>
      api.get(`/sessions/${session!.id}/notes/coverage`, NoteCoverageSchema, {
        signal,
      }),
    enabled: !!session && !!health?.noteCoverage,
    placeholderData: keepPreviousData,
  });
  const report = coverage.data;
  const [open, setOpen] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const review = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (open) dialog.current?.showModal();
    else dialog.current?.close();
  }, [open]);
  function closeReview() {
    setOpen(false);
    review.current?.focus();
  }
  const [repairing, setRepairing] = useState(false);
  const [repairError, setRepairError] = useState("");
  const [attempted, setAttempted] = useState(false);
  const [repairNotice, setRepairNotice] = useState("");
  const error =
    repairError ||
    coverage.error?.message ||
    (!attempted && latestRevision?.status === "failed"
      ? latestRevision.summary
      : "");
  const deferredNotice =
    "No note changes were saved. The AI deferred this batch for more context; the reference gaps remain open.";
  const notice =
    repairNotice ||
    (!attempted && latestRevision?.summary?.startsWith("No section change")
      ? deferredNotice
      : "");
  // A repair still running when the user leaves the session must not reload it afterwards.
  const repairRequest = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => repairRequest.current?.abort(), []);

  async function repair() {
    if (!session || !report || repairing) return;
    const controller = new AbortController();
    repairRequest.current = controller;
    setRepairing(true);
    setAttempted(true);
    setRepairError("");
    setRepairNotice("");
    try {
      const result = await api.post(
        `/sessions/${session.id}/notes/repair`,
        { basedOnVersion: report.version },
        SavedNoteVersionSchema,
        { signal: controller.signal },
      );
      if (!controller.signal.aborted) {
        if (result.version === report.version) setRepairNotice(deferredNotice);
        await refreshWorkspace(session.id);
        await queryClient.invalidateQueries({
          queryKey: ["noteCoverage", session.id],
        });
      }
    } catch (e) {
      if (!controller.signal.aborted)
        setRepairError(e instanceof Error ? e.message : String(e));
    } finally {
      if (!controller.signal.aborted) setRepairing(false);
    }
  }
  if (!session) return null;
  if (!health?.noteCoverage)
    return health?.sectionNotes ? (
      <button
        type="button"
        disabled
        aria-label="Reference checks unavailable"
        title="Restart the local API, then reload to check references."
        className="grid size-8 shrink-0 place-items-center rounded-full border border-line text-muted"
      >
        ?
      </button>
    ) : null;
  const missing = report?.unreferenced ?? 0;
  const repairDisabled =
    repairing ||
    !!busy ||
    workspaceLoading ||
    !report ||
    report.version !== session.noteVersion ||
    draftDirty ||
    coverage.isPlaceholderData;
  return (
    <>
      <aside className="shrink-0" aria-label="Transcript reference coverage">
        <button
          ref={review}
          type="button"
          onClick={() => setOpen(true)}
          aria-haspopup="dialog"
          aria-label="Review references"
          title={
            repairing
              ? "Repairing references"
              : error
                ? "Reference repair needs attention"
                : missing
                  ? missing.toLocaleString() + " transcript parts without links"
                  : "Review note references"
          }
          className={
            "grid size-8 place-items-center rounded-full border text-[15px] font-bold " +
            (missing || error
              ? "border-amber-200 bg-amber-50 text-amber-800"
              : "border-line bg-white text-muted")
          }
        >
          <span aria-hidden="true">
            {repairing || (!report && !error)
              ? "…"
              : missing || error
                ? "!"
                : "✓"}
          </span>
          <span className="sr-only">
            {missing.toLocaleString()} transcript parts without links
          </span>
        </button>
      </aside>
      <Dialog
        ref={dialog}
        aria-labelledby="reference-review-title"
        aria-describedby="reference-review-description"
        className="max-h-[min(85dvh,720px)] w-[min(600px,calc(100vw-24px))] overflow-hidden open:flex open:flex-col"
        onCancel={(event) => {
          event.preventDefault();
          closeReview();
        }}
      >
        <header className="flex shrink-0 items-center justify-between gap-3 border-b border-line px-5 py-4">
          <div>
            <h2 id="reference-review-title" className="text-[16px] font-bold">
              Review note references
            </h2>
            <p className="mt-0.5 text-[11px] text-muted">
              Saved notes · v{report?.version ?? session.noteVersion}
            </p>
          </div>
          <IconButton aria-label="Close reference review" onClick={closeReview}>
            ×
          </IconButton>
        </header>
        <div
          className="min-h-0 overflow-y-auto overscroll-contain px-5 py-4 text-[13px]"
          data-testid="reference-review-body"
        >
          <p
            id="reference-review-description"
            className="leading-relaxed text-muted"
          >
            Missing links can mean missing note content or missing citations.
            Review the passages before repairing.
          </p>
          {error && (
            <div
              className="my-4 rounded-lg border border-red-200 bg-red-50 p-3 text-red-800"
              role="alert"
            >
              {error.includes("unknown term reference") ? (
                <>
                  <p className="font-semibold">
                    The AI returned an invalid explanation link.
                  </p>
                  <p className="mt-1">
                    Playback rejected the update to protect your saved notes.
                    Retrying requests a new AI result; it may fail again.
                  </p>
                  <details className="mt-2 text-[11px]">
                    <summary className="cursor-pointer">Error details</summary>
                    <p className="mt-1">{error}</p>
                  </details>
                </>
              ) : (
                error
              )}
            </div>
          )}
          {notice && !repairing && (
            <p
              role="status"
              className="my-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-amber-900"
            >
              {notice}
            </p>
          )}
          {coverage.error && (
            <Button
              onClick={() => void coverage.refetch()}
              disabled={coverage.isFetching}
            >
              Retry reference check
            </Button>
          )}
          {report && (
            <>
              <dl className="my-4 grid grid-cols-3 divide-x divide-line rounded-lg border border-line bg-white py-3 text-center">
                <div>
                  <dt className="text-[11px] text-muted">Linked</dt>
                  <dd className="mt-1 text-[20px] font-semibold">
                    {report.referenced.toLocaleString()}
                  </dd>
                </div>
                <div>
                  <dt className="text-[11px] text-muted">Without links</dt>
                  <dd className="mt-1 text-[20px] font-semibold text-amber-800">
                    {missing.toLocaleString()}
                  </dd>
                </div>
                <div>
                  <dt className="text-[11px] text-muted">
                    Gaps over {report.largeGapMs / 60000} min
                  </dt>
                  <dd className="mt-1 text-[20px] font-semibold">
                    {report.largeGaps}
                  </dd>
                </div>
              </dl>
              <p className="text-[11px] text-muted">
                {report.total.toLocaleString()} transcript parts checked ·{" "}
                {report.suppressed.toLocaleString()} intentionally excluded
              </p>
              {report.completedWithoutReference > 0 && (
                <p className="mt-3 rounded-lg border-l-2 border-amber-400 bg-amber-50 p-3 text-[12px] leading-relaxed text-amber-900">
                  {report.completedWithoutReference.toLocaleString()} parts were
                  previously marked processed, but have no reference in the
                  saved notes.
                </p>
              )}
              {report.gaps.length > 0 && (
                <section
                  className="mt-5"
                  aria-labelledby="reference-gaps-title"
                >
                  <h3 id="reference-gaps-title" className="mb-2 font-semibold">
                    Passages to review
                  </h3>
                  <ul className="divide-y divide-line rounded-lg border border-line bg-white">
                    {report.gaps.slice(0, 12).map((gap) => (
                      <li className="p-3" key={gap.sourceIds[0]}>
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <button
                            className="rounded text-left font-semibold text-accent underline-offset-4 hover:underline"
                            onClick={() => {
                              closeReview();
                              onOpenSource({
                                kind: "transcript",
                                id: gap.sourceIds[0],
                              });
                            }}
                          >
                            {formatMinutes(gap.startMs)}–
                            {formatMinutes(gap.endMs)}
                            <span className="sr-only">: open transcript</span>
                          </button>
                          {gap.large && (
                            <span className="rounded-full bg-amber-50 px-2 py-0.5 text-[10px] font-semibold text-amber-900">
                              Over {report.largeGapMs / 60000} min
                            </span>
                          )}
                        </div>
                        <p className="mt-1 text-[12px] text-muted">
                          {audioSourceLabel(gap.sourceId)} ·{" "}
                          {gap.sourceIds.length.toLocaleString()} parts
                        </p>
                        {!!gap.deferred && (
                          <p className="mt-1 text-[11px] text-muted">
                            {gap.deferred.toLocaleString()} awaiting review or
                            more speech
                          </p>
                        )}
                      </li>
                    ))}
                  </ul>
                  {report.gaps.length > 12 && (
                    <p className="mt-2 text-[11px] text-muted">
                      {report.gaps.length - 12} more gaps. Repair starts with
                      the earliest unaddressed passage.
                    </p>
                  )}
                </section>
              )}
              {!missing && (
                <p className="mt-4 text-muted">
                  References show which passages were used. They do not verify
                  the completeness of explanations, examples or formulas.
                </p>
              )}
            </>
          )}
        </div>
        {!!missing && (
          <footer className="flex shrink-0 flex-wrap items-center justify-between gap-3 border-t border-line bg-white px-5 py-4">
            <p className="max-w-72 text-[11px] leading-relaxed text-muted">
              {draftDirty
                ? "Save your edits before repairing."
                : "Repairs one batch and keeps existing notes. Deferred passages stay available for review."}
            </p>
            <Button
              variant="primary"
              disabled={repairDisabled}
              className="disabled:opacity-50"
              onClick={() => void repair()}
            >
              {repairing ? "Repairing next gap…" : "Repair next gap with AI"}
            </Button>
          </footer>
        )}
      </Dialog>
    </>
  );
}
