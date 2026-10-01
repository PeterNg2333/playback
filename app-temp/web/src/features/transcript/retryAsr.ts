import { api } from "../../pages/api";
import { runAction } from "../../pages/store";
import type { Session } from "../../types/api";
import { refreshWorkspace } from "../library/refreshWorkspace";

const CHUNKS_PER_REQUEST = 100;

// Sends saved audio parts back to speech recognition after automatic retries stopped.
export function retryAsr(session: Session, chunkIds: string[]) {
  return runAction("retry-asr", async () => {
    for (let index = 0; index < chunkIds.length; index += CHUNKS_PER_REQUEST)
      await api(`/sessions/${session.id}/chunks/retry`, "POST", {
        chunkIds: chunkIds.slice(index, index + CHUNKS_PER_REQUEST),
      });
    await refreshWorkspace(session.id);
  });
}
