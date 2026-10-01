import { api } from "../../lib/backend/client";
import { runAction, usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";

// Lets the user pick a text file and adds it to the session's teaching materials.
export async function attachMaterial(session: Session | null) {
  if (!session || usePlaybackStore.getState().workspaceLoading) return;
  const file = await pickTextFile();
  if (!file) return;
  await runAction("attach", async () => {
    await api.post(`/sessions/${session.id}/materials`, {
      name: file.name,
      text: await file.text(),
    });
    await refreshWorkspace(session.id);
  });
}

function pickTextFile() {
  return new Promise<File | null>((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = ".txt,.md,text/plain,text/markdown";
    input.hidden = true;
    document.body.append(input);
    input.onchange = () => {
      const selected = input.files?.[0] || null;
      input.remove();
      resolve(selected);
    };
    input.oncancel = () => {
      input.remove();
      resolve(null);
    };
    input.click();
  });
}
