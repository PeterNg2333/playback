import { useEffect, useState } from "react";
import type { Session } from "../../lib/backend/schemas";

export function NoteAdmissionStatus({ session }: { session: Session }) {
  const state = session.noteAdmission;
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  if (!state) return null;
  const wait =
    session.processingSettings?.effective.maxPendingWaitSeconds ?? 30;
  const age = state.firstPendingAt
    ? Math.max(0, Math.floor((now - Date.parse(state.firstPendingAt)) / 1000))
    : 0;
  const reason = state.admissionReason.replaceAll("-", " ");
  let text = "";
  if (state.status === "wait")
    text = `Waiting for continuation · ${Math.min(age, wait)} s / ${wait} s${age >= wait ? " · deadline reached" : ""}`;
  else if (state.status === "awaiting-asr") text = "Waiting for final ASR";
  else if (state.status === "writing" || state.status === "allowed")
    text = `Writing notes · ${reason}`;
  else if (state.status === "failed")
    text =
      state.attempts >= 3
        ? "Notes failed · retry with Revise with AI"
        : "Notes failed · retry pending; sources retained";
  else if (state.status === "deferred")
    text =
      "Waiting for continuation · input retained; no repeated automatic request";
  else if (state.status === "completed" && reason)
    text = `Notes updated · ${reason}`;
  if (state.flushRequested) text += `${text ? " · " : ""}Stop flush pending`;
  if (!text) return null;
  return (
    <p
      role="status"
      className="mx-4.5 mb-2.5 text-[11px] text-muted"
      title="The wait limit is an admission deadline. Provider execution takes additional time."
    >
      {text}
    </p>
  );
}
