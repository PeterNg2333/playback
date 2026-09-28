import { useEffect, useState } from "react";
import { ActivitySchema, type Activity } from "../types/api";
import { api } from "./api";

export function useActivity(sessionId?: string, enabled = false): { items: Activity[]; error?: string } {
  const [state, setState] = useState<{ id: string; items: Activity[]; error?: string }>();
  useEffect(() => {
    if (!sessionId || !enabled) return;
    let active = true, pending = false;
    const poll = async () => {
      if (pending) return;
      pending = true;
      try {
        const items = await api(`/sessions/${sessionId}/activity`, "GET", undefined, ActivitySchema.array());
        if (active) setState({ id: sessionId, items });
      } catch (error) {
        if (active) setState({ id: sessionId, items: [], error: error instanceof Error ? error.message : String(error) });
      } finally { pending = false; }
    };
    void poll();
    const timer = setInterval(() => void poll(), 700);
    return () => { active = false; clearInterval(timer); };
  }, [sessionId, enabled]);
  return state && state.id === sessionId ? state : { items: [] };
}
