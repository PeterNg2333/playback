import { useEffect, useRef, useState } from "react";
import { api } from "../../lib/backend/client";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { Button } from "../../components/Button";

// Asks the AI to reorganise one saved notes section. Leaving the session cancels the request.
export function OrganizeSection({
  session,
  draftDirty,
}: {
  session: Session;
  draftDirty: boolean;
}) {
  const [sectionId, setSectionId] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  const request = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => request.current?.abort(), []);
  const sections = session.currentNote?.sections ?? [];

  async function organize() {
    const controller = new AbortController();
    request.current = controller;
    setPending(true);
    setError("");
    try {
      await api.post(
        `/sessions/${session.id}/notes/organize`,
        { sectionId, basedOnVersion: session.noteVersion },
        undefined,
        { signal: controller.signal },
      );
      if (!controller.signal.aborted) await refreshWorkspace(session.id);
    } catch (e) {
      if (!controller.signal.aborted)
        setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (!controller.signal.aborted) setPending(false);
    }
  }

  if (!sections.length) return null;
  return (
    <>
      <label className="sr-only" htmlFor="organize-section">
        Section to organize
      </label>
      {/* Keeps the browser's own drop-down look, within a width the toolbar can wrap. */}
      <select
        className="max-w-55 min-w-0 rounded-[revert] [border:revert] bg-[revert] [font:revert] text-[revert]"
        id="organize-section"
        value={sectionId}
        onChange={(e) => setSectionId(e.target.value)}
      >
        <option value="">Select section…</option>
        {sections.map((x) => (
          <option key={x.id} value={x.id}>
            {x.title}
            {x.userEdited ? " (user edited)" : ""}
          </option>
        ))}
      </select>
      <Button
        disabled={!sectionId || pending || draftDirty}
        onClick={() => void organize()}
      >
        Organize section
      </Button>
      {error && <p role="alert">{error}</p>}
    </>
  );
}
