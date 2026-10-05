import { useState } from "react";
import { api } from "../../lib/backend/client";
import {
  SavedNoteVersionSchema,
  type Session,
} from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";

type Draft = {
  sessionId: string | null;
  // What the editor shows.
  text: string;
  // The saved notes the text is compared with to find unsaved changes.
  saved: string;
  // The saved version the text was started from; a save is rejected if the notes moved on.
  baseVersion: number;
  // The saved notes this draft has already taken into account.
  seenVersion: number;
  seenMarkdown: string;
  // Text set aside by "Load current notes", so the user can get it back.
  setAside?: { text: string; baseVersion: number };
};

const emptyDraft: Draft = {
  sessionId: null,
  text: "",
  saved: "",
  baseVersion: 0,
  seenVersion: 0,
  seenMarkdown: "",
};

// A new saved version replaces the editor text only when the user has no unsaved changes.
function takeSavedNotes(draft: Draft, session: Session): Draft {
  const switched = draft.sessionId !== session.id;
  const unsaved = !switched && draft.text !== draft.saved;
  return {
    sessionId: session.id,
    text: unsaved ? draft.text : session.noteMarkdown,
    saved: session.noteMarkdown,
    baseVersion:
      unsaved && draft.text !== session.noteMarkdown
        ? draft.baseVersion
        : session.noteVersion,
    seenVersion: session.noteVersion,
    seenMarkdown: session.noteMarkdown,
    setAside: switched ? undefined : draft.setAside,
  };
}

// The notes text the user is editing, which saved version it is based on, and saving it.
export function useNoteDraft(session: Session | null) {
  const [stored, setDraft] = useState(emptyDraft);
  let draft = stored;
  if (
    session &&
    (stored.sessionId !== session.id ||
      stored.seenVersion !== session.noteVersion ||
      stored.seenMarkdown !== session.noteMarkdown)
  ) {
    // Adjust during render so the editor never shows one frame of a stale draft.
    draft = takeSavedNotes(stored, session);
    setDraft(draft);
  }
  const isDirty = draft.text !== draft.saved;
  const conflict =
    !!session && isDirty && draft.baseVersion !== session.noteVersion;

  async function saveText(text: string) {
    const saved = await api.post(
      `/sessions/${session!.id}/notes`,
      { markdown: text, basedOnVersion: draft.baseVersion },
      SavedNoteVersionSchema,
    );
    setDraft((current) => ({
      ...current,
      baseVersion: saved.version,
      saved: text,
    }));
    await refreshWorkspace(session!.id);
  }

  return {
    text: draft.text,
    setText: (text: string) => setDraft((current) => ({ ...current, text })),
    isDirty,
    conflict,
    setAside: draft.setAside,
    save: () => saveText(draft.text),
    // Saves unsaved edits first so the AI revises what the user sees.
    reviseWithAi: async () => {
      if (isDirty) await saveText(draft.text);
      await api.post(`/sessions/${session!.id}/notes/generate`);
      await refreshWorkspace(session!.id);
    },
    // Shows the current saved notes and keeps the user's text for recovery.
    takeSavedVersion: () =>
      setDraft((current) => ({
        ...current,
        setAside: { text: current.text, baseVersion: current.baseVersion },
        baseVersion: session!.noteVersion,
        text: session!.noteMarkdown,
      })),
    recoverSetAside: () =>
      setDraft((current) =>
        current.setAside
          ? {
              ...current,
              text: current.setAside.text,
              baseVersion: current.setAside.baseVersion,
              setAside: undefined,
            }
          : current,
      ),
  };
}
