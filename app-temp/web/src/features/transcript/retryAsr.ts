import { api } from "../../lib/backend/client";
import { runAction } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";

const CHUNKS_PER_REQUEST = 100;

// Sends saved audio parts back to speech recognition after automatic retries stopped.
export function retryAsr(session: Session, chunkIds: string[]) {
  return runAction("retry-asr", async () => {
    for (let index = 0; index < chunkIds.length; index += CHUNKS_PER_REQUEST)
      await api.post(`/sessions/${session.id}/chunks/retry`, {
        chunkIds: chunkIds.slice(index, index + CHUNKS_PER_REQUEST),
      });
    await refreshWorkspace(session.id);
  });
}
