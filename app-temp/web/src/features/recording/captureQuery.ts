import { queryOptions, useQuery } from "@tanstack/react-query";
import { api } from "../../lib/backend/client";
import {
  CaptureStatusSchema,
  type CaptureStatus,
} from "../../lib/backend/schemas";

const STATUS_TIMEOUT_MS = 10_000;

// The local recorder's state: whether it records, for which session, and the speech it hears now.
export const captureQuery = queryOptions({
  queryKey: ["capture"],
  queryFn: ({ signal }) =>
    api.get("/capture/status", CaptureStatusSchema, {
      signal: AbortSignal.any([signal, AbortSignal.timeout(STATUS_TIMEOUT_MS)]),
    }),
  staleTime: Infinity,
});

// Reads one part of the recorder status; the component re-renders only when that part changes.
export function useCaptureStatus<T>(select: (status: CaptureStatus) => T) {
  return useQuery({ ...captureQuery, select }).data;
}
