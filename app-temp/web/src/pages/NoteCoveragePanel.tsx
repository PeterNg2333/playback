import { useEffect, useRef, useState } from "react";
import { z } from "zod";
import { api } from "./api";
import type { PlaybackController } from "./handlers";

const ReportSchema = z.object({
  version: z.number(),
  total: z.number(),
  referenced: z.number(),
  suppressed: z.number(),
  unreferenced: z.number(),
  completedWithoutReference: z.number(),
  largeGaps: z.number(),
  largeGapMs: z.number(),
  gaps: z.array(
    z.object({
      sourceId: z.string(),
      startMs: z.number(),
      endMs: z.number(),
      speechMs: z.number(),
      sourceIds: z.array(z.string()),
      deferred: z.number(),
      completedWithoutReference: z.number(),
      large: z.boolean(),
    }),
  ),
});
type Report = z.infer<typeof ReportSchema>;
const time = (ms: number) =>
  `${Math.floor(ms / 60000)}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")}`;

export function NoteCoveragePanel({ model }: { model: PlaybackController }) {
  const { session } = model;
  const [report, setReport] = useState<Report>();
  const [error, setError] = useState("");
  const [repairing, setRepairing] = useState(false);
  const [revision, setRevision] = useState(0);
  const repairRequest = useRef<AbortController | undefined>(undefined);
  useEffect(() => {
    repairRequest.current?.abort();
    setRepairing(false);
    setReport(undefined);
    setError("");
    return () => repairRequest.current?.abort();
  }, [session?.id]);
  useEffect(() => {
    if (!session || !model.health?.noteCoverage) return;
    const controller = new AbortController();
    void api(
      `/sessions/${session.id}/notes/coverage`,
      "GET",
      undefined,
      ReportSchema,
      controller.signal,
    )
      .then((value) => {
        if (!controller.signal.aborted) {
          setReport(value);
          setError("");
        }
      })
      .catch((e) => {
        if (!controller.signal.aborted)
          setError(e instanceof Error ? e.message : String(e));
      });
    return () => controller.abort();
  }, [
    session?.id,
    session?.noteVersion,
    session?.transcripts.length,
    model.health?.noteCoverage,
    revision,
  ]);
  async function repair() {
    if (!session || !report || repairing) return;
    const controller = new AbortController();
    repairRequest.current = controller;
    setRepairing(true);
    setError("");
    try {
      await api(
        `/sessions/${session.id}/notes/repair`,
        "POST",
        { basedOnVersion: report.version },
        undefined,
        controller.signal,
      );
      if (!controller.signal.aborted) {
        await model.refresh(session.id);
        setRevision((x) => x + 1);
      }
    } catch (e) {
      if (!controller.signal.aborted)
        setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (!controller.signal.aborted) setRepairing(false);
    }
  }
  if (!session) return null;
  if (!model.health?.noteCoverage)
    return model.health?.sectionNotes ? (
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
                  model.jump({ kind: "transcript", id: gap.sourceIds[0] })
                }
              >
                {time(gap.startMs)}–{time(gap.endMs)} · {gap.sourceId} ·{" "}
                {gap.sourceIds.length} parts{gap.large ? " · large gap" : ""}
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
                !!model.busy ||
                model.workspaceLoading ||
                report.version !== session.noteVersion ||
                model.markdown !== model.savedMarkdown.current
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
