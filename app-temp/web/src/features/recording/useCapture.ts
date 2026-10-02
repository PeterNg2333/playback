import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { queryClient } from "../../lib/queryClient";
import { useHealth } from "../../lib/useHealth";
import { api } from "../../lib/backend/client";
import { runAction, showError, usePlaybackStore } from "../../lib/store";
import { CaptureStatusSchema, type Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { captureQuery } from "./captureQuery";

const STATUS_POLL_MS = 250;
const LEVEL_SAMPLES = 20;
const silence = Array<number>(LEVEL_SAMPLES).fill(0);

type RecorderCommand = "start" | "stop" | "pause" | "resume";

// Polls the local recorder, keeps the last few seconds of input level for the meter,
// and sends start/pause/resume/stop for the selected session.
export function useCapture(session: Session | null) {
  const { data, dataUpdatedAt, error, errorUpdatedAt } = useQuery({
    ...captureQuery,
    refetchInterval: STATUS_POLL_MS,
  });
  const health = useHealth();
  const recordingMode = usePlaybackStore((state) => state.recordingMode);
  const [levels, setLevels] = useState(silence);

  useEffect(() => {
    if (data?.state !== "recording") {
      setLevels(silence);
      return;
    }
    const level = Math.max(0, ...Object.values(data.levels || {}));
    const speaking = data.activeSegments?.some((segment) => segment.streaming);
    setLevels((previous) => [...previous.slice(1), speaking ? level : 0]);
  }, [dataUpdatedAt]);
  useEffect(() => {
    if (error) showError(error);
  }, [errorUpdatedAt]);

  async function record(command: RecorderCommand) {
    if (command === "start" && !session) return;
    await runAction("capture", async () => {
      if (command === "start" && !health?.recordingSourceSelection)
        throw new Error(
          "Restart the API to record with the selected audio source",
        );
      const status = await api.post(
        `/capture/${command}`,
        command === "start"
          ? { sessionId: session!.id, sourceMode: recordingMode }
          : undefined,
        CaptureStatusSchema,
      );
      // A status poll sent before the command must not overwrite its result.
      await queryClient.cancelQueries({ queryKey: captureQuery.queryKey });
      queryClient.setQueryData(captureQuery.queryKey, status);
      await refreshWorkspace(session?.id);
    });
  }

  return { status: data ?? null, levels, record };
}
