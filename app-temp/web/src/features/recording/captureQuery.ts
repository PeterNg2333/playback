import { queryOptions, useQuery } from "@tanstack/react-query";
import { api } from "../../pages/api";
import { CaptureStatusSchema, type CaptureStatus } from "../../types/api";

const STATUS_TIMEOUT_MS = 10_000;

// The local recorder's state: whether it records, for which session, and the speech it hears now.
export const captureQuery = queryOptions({
  queryKey: ["capture"],
  queryFn: ({ signal }) =>
    api(
      "/capture/status",
      "GET",
      undefined,
      CaptureStatusSchema,
      AbortSignal.any([signal, AbortSignal.timeout(STATUS_TIMEOUT_MS)]),
    ),
  staleTime: Infinity,
});

// Reads one part of the recorder status; the component re-renders only when that part changes.
export function useCaptureStatus<T>(select: (status: CaptureStatus) => T) {
  return useQuery({ ...captureQuery, select }).data;
}
