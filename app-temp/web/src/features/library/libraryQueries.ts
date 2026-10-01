import { queryOptions } from "@tanstack/react-query";
import { queryClient } from "../../lib/queryClient";
import { healthQuery } from "../../lib/useHealth";
import { api } from "../../lib/backend/client";
import { readSession } from "../../lib/backend/sessionSync";
import {
  GroupSchema,
  SessionSchema,
  SessionSummarySchema,
  type Session,
} from "../../lib/backend/schemas";

const LIST_TIMEOUT_MS = 30_000;
// A three-hour session is large; release it soon after the user switches away.
const RELEASE_UNSHOWN_SESSION_MS = 1_000;

export const sessionListQuery = queryOptions({
  queryKey: ["sessions"],
  queryFn: ({ signal }) =>
    api.get("/sessions", SessionSummarySchema.array(), {
      signal: AbortSignal.any([signal, AbortSignal.timeout(LIST_TIMEOUT_MS)]),
    }),
  staleTime: Infinity,
});

export const groupsQuery = queryOptions({
  queryKey: ["groups"],
  queryFn: ({ signal }) =>
    api.get("/groups", GroupSchema.array(), {
      signal: AbortSignal.any([signal, AbortSignal.timeout(LIST_TIMEOUT_MS)]),
    }),
  staleTime: Infinity,
});

export const sessionQuery = (id: string) =>
  queryOptions({
    queryKey: ["session", id],
    queryFn: ({ signal }) => readFullSession(id, signal),
    gcTime: RELEASE_UNSHOWN_SESSION_MS,
  });

// The server keeps a reader cursor per session and advances it as it sends each page,
// so a read that did not finish leaves its cursor unusable. A read takes the cursor;
// only a finished read stores the next one.
const syncCursors = new Map<string, string>();

async function readFullSession(id: string, signal: AbortSignal) {
  if (!queryClient.getQueryData(healthQuery.queryKey)?.sessionSync)
    return api.get("/sessions/" + id, SessionSchema, { signal });
  const previous: Session | null =
    queryClient.getQueryData(sessionQuery(id).queryKey) ?? null;
  const cursor = previous ? syncCursors.get(id) : undefined;
  syncCursors.delete(id);
  const result = await readSession(id, previous, cursor, signal);
  syncCursors.set(id, result.cursor);
  return result.session;
}
