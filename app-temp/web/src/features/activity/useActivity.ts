import { useQuery } from "@tanstack/react-query";
import { ActivitySchema, type Activity } from "../../types/api";
import { api } from "../../pages/api";

const ACTIVITY_POLL_MS = 700;
const ACTIVITY_TIMEOUT_MS = 30_000;
const noActivity: Activity[] = [];
const notesActivity = ActivitySchema.omit({ promptText: true })
  .array()
  .max(100);

// The session's recent AI executions, polled while the backend records them.
export function useActivity(
  sessionId?: string,
  enabled = false,
): { items: Activity[]; error?: string } {
  const { data, error } = useQuery({
    queryKey: ["activity", sessionId],
    queryFn: ({ signal }) =>
      api(
        `/sessions/${sessionId}/activity?includePrompt=false`,
        "GET",
        undefined,
        notesActivity,
        AbortSignal.any([signal, AbortSignal.timeout(ACTIVITY_TIMEOUT_MS)]),
      ),
    enabled: !!sessionId && enabled,
    refetchInterval: ACTIVITY_POLL_MS,
  });
  if (!sessionId || !enabled) return { items: noActivity };
  return { items: data ?? noActivity, error: error?.message };
}
