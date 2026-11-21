import { api } from "../../lib/backend/client";
import { runAction, usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { extractMaterialText, MATERIAL_ACCEPT } from "./extractText";

// Lets the user pick a file (text, Word, PowerPoint, Excel or PDF) and adds its text to the session's materials.
export async function attachMaterial(session: Session | null) {
  if (!session || usePlaybackStore.getState().workspaceLoading) return;
  const file = await pickFile();
  if (!file) return;
  await runAction("attach", async () => {
    const { text, truncated } = await extractMaterialText(file);
    const name =
      file.name.length > 120 ? file.name.slice(0, 117) + "…" : file.name;
    await api.post(`/sessions/${session.id}/materials`, { name, text });
    await refreshWorkspace(session.id);
    if (truncated)
      usePlaybackStore.setState({
        error: `${file.name} is long: only its first 250,000 characters were kept.`,
      });
  });
}

function pickFile() {
  return new Promise<File | null>((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = MATERIAL_ACCEPT;
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
