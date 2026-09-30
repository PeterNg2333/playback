import { useEffect, useState } from "react";
import { ActivitySchema, type Activity } from "../types/api";
import { api } from "./api";

const empty = { items: [] as Activity[] };
const notesActivity = ActivitySchema.omit({ promptText: true }).array().max(100);
function sameRecord(previous: Activity, next: Activity) {
  for (const field in next) {
    const key = field as keyof Activity;
    if (key === "sourceIds") {
      if (previous.sourceIds.length !== next.sourceIds.length || previous.sourceIds.some((id, i) => id !== next.sourceIds[i])) return false;
    } else if (previous[key] !== next[key]) return false;
  }
  for (const field in previous) if (!(field in next)) return false;
  return true;
}
export function useActivity(sessionId?: string, enabled = false): { items: Activity[]; error?: string } {
  const [state, setState] = useState<{ id: string; items: Activity[]; error?: string }>();
  useEffect(() => {
    setState(undefined);
    if (!sessionId || !enabled) return;
    let active = true, pending = false;
    let request: AbortController | undefined;
    const poll = async () => {
      if (pending) return;
      pending = true;
      request = new AbortController();
      try {
        const items = await api(`/sessions/${sessionId}/activity?includePrompt=false`, "GET", undefined, notesActivity, AbortSignal.any([request.signal, AbortSignal.timeout(30000)]));
        if (active) setState(previous => {
          if (previous?.id !== sessionId) return { id: sessionId, items };
          const byId = new Map(previous.items.map(item => [item.id, item]));
          const shared = items.map(item => { const prior = byId.get(item.id); return prior && sameRecord(prior, item) ? prior : item; });
          return !previous.error && shared.length === previous.items.length && shared.every((item, i) => item === previous.items[i])
            ? previous : { id: sessionId, items: shared };
        });
      } catch (error) {
        if (active) setState(previous => ({ id: sessionId, items: previous?.id === sessionId ? previous.items : [], error: error instanceof Error ? error.message : String(error) }));
      } finally { pending = false; }
    };
    void poll();
    const timer = setInterval(() => void poll(), 700);
    return () => { active = false; clearInterval(timer); request?.abort(); };
  }, [sessionId, enabled]);
  return enabled && state && state.id === sessionId ? state : empty;
}
