import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { queryClient } from "../../lib/queryClient";
import { useHealth } from "../../lib/useHealth";
import { api } from "../../lib/backend/client";
import { runAction, showError, usePlaybackStore } from "../../lib/store";
import {
  CaptureStatusSchema,
  type CaptureStatus,
  type Health,
  type RecorderKind,
  type Session,
} from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { browserRecorder, browserRecording } from "./browserRecorder";
import { captureQuery } from "./captureQuery";

const STATUS_POLL_MS = 250;
const LEVEL_SAMPLES = 20;
const silence = Array<number>(LEVEL_SAMPLES).fill(0);
const idle: CaptureStatus = { state: "idle", bytes: {} };

type RecorderCommand = "start" | "stop" | "pause" | "resume";

// The recorders on offer: this browser when the API accepts uploaded streams and the browser can record, and the
// API's Windows recorder unless the API says it cannot capture (a Linux container, for example).
export function recorderKinds(health: Health | null): RecorderKind[] {
  if (!health) return ["server"];
  const kinds: RecorderKind[] = [];
  if (health.uploadStreams && browserRecording().microphone)
    kinds.push("browser");
  if (health.localCapture !== false) kinds.push("server");
  return kinds;
}

// Polls the recorder, keeps the last few seconds of input level for the meter, and sends start/pause/resume/stop for
// the selected session to the browser recorder or the API's Windows recorder.
export function useCapture(session: Session | null) {
  const { data, dataUpdatedAt, error, errorUpdatedAt } = useQuery({
    ...captureQuery,
    refetchInterval: STATUS_POLL_MS,
  });
  const health = useHealth();
  const recordingMode = usePlaybackStore((state) => state.recordingMode);
  const chosenKind = usePlaybackStore((state) => state.recorderKind);
  const [levels, setLevels] = useState(silence);
  const kinds = recorderKinds(health);
  const kind: RecorderKind | null = browserRecorder.active
    ? "browser"
    : data && data.state !== "idle"
      ? "server"
      : kinds.includes(chosenKind)
        ? chosenKind
        : (kinds[0] ?? null);
  const systemAudio = kind !== "browser" || browserRecording().systemAudio;
  const mode = systemAudio ? recordingMode : "microphone";

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

  async function browser(command: RecorderCommand) {
    if (command === "start") await browserRecorder.start(session!, mode);
    else await browserRecorder[command]();
    return browserRecorder.status() ?? idle;
  }

  async function record(command: RecorderCommand) {
    if (command === "start" && !session) return;
    await runAction("capture", async () => {
      let status: CaptureStatus;
      if (kind === "browser") status = await browser(command);
      else {
        if (command === "start" && !health?.recordingSourceSelection)
          throw new Error(
            "Restart the API to record with the selected audio source",
          );
        status = await api.post(
          `/capture/${command}`,
          command === "start"
            ? { sessionId: session!.id, sourceMode: mode }
            : undefined,
          CaptureStatusSchema,
        );
      }
      // A status poll sent before the command must not overwrite its result.
      await queryClient.cancelQueries({ queryKey: captureQuery.queryKey });
      queryClient.setQueryData(captureQuery.queryKey, status);
      await refreshWorkspace(session?.id);
    });
  }

  return {
    status: data ?? null,
    levels,
    record,
    kinds,
    kind,
    mode,
    systemAudio,
  };
}
