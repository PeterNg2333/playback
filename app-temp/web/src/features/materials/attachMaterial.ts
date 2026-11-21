import { api } from "../../lib/backend/client";
import { runAction, usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { extractMaterialText, MATERIAL_ACCEPT } from "./extractText";

// The note model reads the session's materials only up to this many characters in all (NoteWorkflow.MaxMaterialChars).
export const NOTE_MATERIAL_CHARS = 12_000;

// How the materials fit the notes' budget once a file is added: their total, and which files the notes read in part
// or not at all (materials are read in the order they were added).
export type MaterialBudget = {
  added: string;
  total: number;
  partly: string | null;
  unread: string[];
};
export function materialBudget(
  materials: { name: string; text: string }[],
  added: string,
): MaterialBudget {
  let left = NOTE_MATERIAL_CHARS;
  let partly: string | null = null;
  const unread: string[] = [];
  for (const material of materials) {
    if (left <= 0) unread.push(material.name);
    else if (material.text.length > left) partly = material.name;
    left -= material.text.length;
  }
  return {
    added,
    total: materials.reduce((sum, x) => sum + x.text.length, 0),
    partly,
    unread,
  };
}

// Lets the user pick a file (text, Word, PowerPoint, Excel or PDF) and adds its text to the session's materials.
// Resolves to the budget when the materials now run past what the notes read.
export async function attachMaterial(session: Session | null) {
  if (!session || usePlaybackStore.getState().workspaceLoading) return null;
  const file = await pickFile();
  if (!file) return null;
  let budget: MaterialBudget | null = null;
  await runAction("attach", async () => {
    const { text, truncated } = await extractMaterialText(file);
    const name =
      file.name.length > 120 ? file.name.slice(0, 117) + "…" : file.name;
    await api.post(`/sessions/${session.id}/materials`, { name, text });
    const materials = [...session.materials, { name, text }];
    if (
      materials.reduce((sum, x) => sum + x.text.length, 0) > NOTE_MATERIAL_CHARS
    )
      budget = materialBudget(materials, name);
    await refreshWorkspace(session.id);
    if (truncated)
      usePlaybackStore.setState({
        error: `${file.name} is long: only its first 250,000 characters were kept.`,
      });
  });
  return budget;
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
