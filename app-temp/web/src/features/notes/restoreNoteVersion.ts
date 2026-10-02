import { api } from "../../lib/backend/client";

// Restoring saves an old version as a new one on top of the version the reader saw.
// The API refuses when the notes changed since then, so a newer AI or user version is never overwritten.
export function restoreNoteVersion(
  sessionId: string,
  version: number,
  basedOnVersion: number,
  signal?: AbortSignal,
) {
  return api.post(
    `/sessions/${sessionId}/notes/${version}/restore?basedOnVersion=${basedOnVersion}`,
    undefined,
    undefined,
    { signal },
  );
}
