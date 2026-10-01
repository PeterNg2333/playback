import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { queryClient } from "../../lib/queryClient";
import { showError, usePlaybackStore } from "../../pages/store";
import type { Session } from "../../types/api";
import { useCaptureStatus } from "../recording/captureQuery";
import { sessionQuery } from "./libraryQueries";

const SESSION_POLL_MS = 4000;

// The session the page shows. It is re-read every few seconds, and as soon as the
// recorder has saved new audio for it.
export function useSelectedSession(): Session | null {
  const id = usePlaybackStore((state) => state.selectedSessionId);
  const { data, error, errorUpdatedAt } = useQuery({
    ...sessionQuery(id ?? ""),
    enabled: id !== null,
    refetchInterval: SESSION_POLL_MS,
    refetchOnMount: false,
  });
  useEffect(() => {
    if (error) showError(error);
  }, [errorUpdatedAt]);

  const audioSavedAt = useCaptureStatus((status) =>
    status.sessionId === id ? (status.lastFinalizedAtMs ?? 0) : 0,
  );
  useEffect(() => {
    if (id && audioSavedAt)
      void queryClient.refetchQueries(
        { queryKey: sessionQuery(id).queryKey, exact: true },
        { cancelRefetch: false },
      );
  }, [id, audioSavedAt]);

  return data ?? null;
}
