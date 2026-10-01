import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { queryClient } from "../../lib/queryClient";
import { api } from "../../lib/backend/client";
import { formatMinutes } from "../../lib/time";
import { useHealth } from "../../lib/useHealth";
import { refreshWorkspace } from "../library/refreshWorkspace";
import {
  NoteCoverageSchema,
  type Evidence,
  type Session,
} from "../../lib/backend/schemas";
import { usePlaybackStore } from "../../lib/store";

// How many transcript parts the saved notes cite, the gaps left, and repairing the earliest gap.
export function NoteCoveragePanel({
  session,
  draftDirty,
  onOpenSource,
}: {
  session: Session | null;
  draftDirty: boolean;
  onOpenSource: (source: Evidence) => void;
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
  const [repairing, setRepairing] = useState(false);
  const [repairError, setRepairError] = useState("");
  const error = repairError || coverage.error?.message;
  // A repair still running when the user leaves the session must not reload it afterwards.
  const repairRequest = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => repairRequest.current?.abort(), []);

  async function repair() {
    if (!session || !report || repairing) return;
    const controller = new AbortController();
    repairRequest.current = controller;
    setRepairing(true);
    setRepairError("");
    try {
      await api.post(
        `/sessions/${session.id}/notes/repair`,
        { basedOnVersion: report.version },
        undefined,
        { signal: controller.signal },
      );
      if (!controller.signal.aborted) {
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
      <aside className="note-coverage">
        Transcript reference checks need the updated API. Stop recording,
        restart the local backend, then reload.
      </aside>
    ) : null;
  return (
    <aside className="note-coverage" aria-label="Transcript reference coverage">
      {error && <p role="alert">Reference coverage: {error}</p>}
      {report && (
        <details>
          <summary>
            {report.unreferenced
              ? `${report.unreferenced} transcript parts without note references · ${report.largeGaps} gaps over ${report.largeGapMs / 60000} min`
              : `${report.referenced}/${report.total} transcript parts referenced`}
          </summary>
          <p>
            {report.referenced}/{report.total} parts referenced;{" "}
            {report.suppressed} intentionally excluded. References show
            traceability; review the explanations, examples and formulas for
            completeness.
          </p>
          {report.completedWithoutReference > 0 && (
            <p>
              {report.completedWithoutReference} parts were marked completed but
              have no reference in the saved notes.
            </p>
          )}
          {report.gaps.slice(0, 12).map((gap) => (
            <div className="note-coverage-gap" key={gap.sourceIds[0]}>
              <button
                className="text-control"
                onClick={() =>
                  onOpenSource({ kind: "transcript", id: gap.sourceIds[0] })
                }
              >
                {formatMinutes(gap.startMs)}–{formatMinutes(gap.endMs)} ·{" "}
                {gap.sourceId} · {gap.sourceIds.length} parts
                {gap.large ? " · large gap" : ""}
              </button>
              {!!gap.deferred && (
                <span>{gap.deferred} awaiting continuation</span>
              )}
            </div>
          ))}
          {report.gaps.length > 12 && (
            <p>
              {report.gaps.length - 12} more gaps; repair starts with the
              earliest.
            </p>
          )}
          {!!report.unreferenced && (
            <button
              className="text-control"
              disabled={
                repairing ||
                !!busy ||
                workspaceLoading ||
                report.version !== session.noteVersion ||
                draftDirty
              }
              onClick={() => void repair()}
            >
              {repairing
                ? "Repairing earliest gap…"
                : "Repair earliest gap with AI"}
            </button>
          )}
          <p>
            Each repair generates one bounded batch and preserves existing
            sections. Remaining gaps stay visible.
          </p>
        </details>
      )}
    </aside>
  );
}
