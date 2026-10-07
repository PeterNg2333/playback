import {useEffect, useState} from "react";
import type {Session} from "../../lib/backend/schemas";

export function OrganizationStatus({session}: {session: Session}) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer);}, []);
  const state = session.organization;
  if (!state || (!state.dirtyRevisions && !state.lastOrganizedAt && state.status === "idle")) return null;
  const cooldown = state.cooldownUntil ? Math.max(0, Math.ceil((Date.parse(state.cooldownUntil) - now) / 1000)) : 0;
  const age = state.dirtySince ? Math.max(0, Math.floor((now - Date.parse(state.dirtySince)) / 1000)) : 0;
  const text = state.status === "organizing" ? "Organizing notes" : state.status === "failed" ? "Organization failed · original text retained" : state.status === "review-required" ? "Rewrite preview needs review · original text retained" : state.status === "protected-scope" ? "Protected notes · organize a selected topic" : "Organization";
  return <p role="status" className="mx-4.5 mb-2.5 text-[11px] text-muted">{text}
    {state.dirtyRevisions > 0 && ` · ${state.dirtyRevisions} changed revisions · ${age} s dirty`}
    {state.dueReason && ` · ${state.dueReason.replaceAll("-", " ")}`}{cooldown > 0 && ` · cooldown ${cooldown} s`}
    {state.lastOrganizedAt && ` · last organized ${new Date(state.lastOrganizedAt).toLocaleTimeString()}`}
  </p>;
}
