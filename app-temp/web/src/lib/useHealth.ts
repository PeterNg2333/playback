import { queryOptions, useQuery } from "@tanstack/react-query";
import { api } from "../pages/api";
import { HealthSchema } from "../types/api";

const HEALTH_TIMEOUT_MS = 30_000;

// Which providers and API features the running backend offers. Read at each workspace refresh.
export const healthQuery = queryOptions({
  queryKey: ["health"],
  queryFn: ({ signal }) =>
    api(
      "/health",
      "GET",
      undefined,
      HealthSchema,
      AbortSignal.any([signal, AbortSignal.timeout(HEALTH_TIMEOUT_MS)]),
    ),
  staleTime: Infinity,
});

export function useHealth() {
  return useQuery(healthQuery).data ?? null;
}
