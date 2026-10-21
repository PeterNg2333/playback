import { api } from "../../lib/backend/client";
import { runAction } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";

// A session rerun: ASR over longer windows of the saved audio, then the notes updated or rebuilt.
export type RerunInput = {
  asr: boolean;
  asrModel: string | null;
  windowSeconds: number;
  notes: "incremental" | "rebuild" | "none";
};

export function startRerun(session: Session, input: RerunInput) {
  return runAction("rerun", async () => {
    await api.post(`/sessions/${session.id}/rerun`, input);
    await refreshWorkspace(session.id);
  });
}

export function cancelRerun(session: Session) {
  return runAction("rerun", async () => {
    await api.post(`/sessions/${session.id}/rerun/cancel`);
    await refreshWorkspace(session.id);
  });
}

// Saved speech a rerun would upload again: every chunk with words, no words or a failed first pass.
export function rerunAudioMinutes(session: Session) {
  const ms = session.chunks
    .filter((chunk) =>
      ["transcribed", "asr-empty", "asr-error", "asr-manual"].includes(
        chunk.status,
      ),
    )
    .reduce((sum, chunk) => sum + (chunk.endMs - chunk.startMs), 0);
  return ms / 60_000;
}
