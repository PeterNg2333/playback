import { usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { cancelRerun } from "./rerun";

const RECENT_MS = 10 * 60_000;

// The session's latest rerun while it runs, and for ten minutes after it ends.
export function RerunStatus({ session }: { session: Session | null }) {
  const busy = usePlaybackStore((state) => state.busy);
  const rerun = session?.rerun;
  if (!session || !rerun) return null;
  const running = rerun.status === "running";
  const ended = rerun.completedAt ? new Date(rerun.completedAt).getTime() : 0;
  if (!running && Date.now() - ended > RECENT_MS) return null;
  const done = rerun.completed + rerun.unchanged + rerun.skipped + rerun.failed;
  const title = running
    ? rerun.cancelRequested
      ? "Stopping rerun…"
      : rerun.asr
        ? `Rerunning · ${done} of ${rerun.windows} passages`
        : "Rebuilding notes…"
    : rerun.status === "completed"
      ? "Rerun finished"
      : rerun.status === "cancelled"
        ? "Rerun stopped"
        : "Rerun failed";
  return (
    <div
      className="flex items-center gap-2 border-b border-line bg-[#f7f7fc] px-4 py-2 text-[11px] text-muted"
      role="status"
      data-testid="rerun-status"
    >
      {running && rerun.asr && (
        <progress
          className="h-1.5 w-20 flex-none accent-accent"
          max={Math.max(1, rerun.windows)}
          value={done}
          aria-label="Rerun progress"
        />
      )}
      <span className="min-w-0 flex-1">
        <strong className="text-ink">{title}</strong> · {rerun.summary}
        {rerun.error && <> · {rerun.error}</>}
      </span>
      {running && !rerun.cancelRequested && (
        <button
          type="button"
          className="flex-none rounded-md border border-line bg-white px-2 py-1 font-bold text-ink disabled:opacity-55"
          disabled={!!busy}
          onClick={() => void cancelRerun(session)}
        >
          Stop
        </button>
      )}
    </div>
  );
}
