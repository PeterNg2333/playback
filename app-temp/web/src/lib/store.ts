import type { Dispatch, SetStateAction } from "react";
import { create } from "zustand";
import { useCallback } from "react";
import type { RecordingMode } from "./backend/schemas";

// Browser-only UI state. Data read from the API lives in the TanStack Query cache.
export type View = "notes" | "transcript";
export type NoteMode = "preview" | "markdown" | "draft";
export type Selection = {
  transcriptId: string;
  text: string;
  startMs: number;
  endMs: number;
};
export type TextDialogState =
  | { kind: "session"; groupId: string | null }
  | { kind: "group" }
  | { kind: "rename-group"; id: string; current: string };

type PlaybackState = {
  selectedSessionId: string | null;
  workspaceLoading: boolean;
  settingsOpen: boolean;
  navOpen: boolean;
  view: View;
  transcriptView: "transcript" | "activity";
  recordingMode: RecordingMode;
  noteMode: NoteMode;
  chatOpen: boolean;
  selection: Selection | null;
  focusMaterialId: string | null;
  question: string;
  busy: string;
  error: string;
  useWeb: boolean;
  textDialog: TextDialogState | null;
  textDialogError: string;
};

export const usePlaybackStore = create<PlaybackState>(() => ({
  selectedSessionId: null,
  workspaceLoading: false,
  settingsOpen: false,
  navOpen: false,
  view: "transcript",
  transcriptView: "transcript",
  recordingMode: "both",
  noteMode: "preview",
  chatOpen: false,
  selection: null,
  focusMaterialId: null,
  question: "",
  busy: "",
  error: "",
  useWeb: false,
  textDialog: null,
  textDialogError: "",
}));

// Keep each field subscription narrow while retaining React's functional setter shape.
export function usePlaybackField<K extends keyof PlaybackState>(
  key: K,
): [PlaybackState[K], Dispatch<SetStateAction<PlaybackState[K]>>] {
  const value = usePlaybackStore((state) => state[key]);
  const update: Dispatch<SetStateAction<PlaybackState[K]>> = useCallback(
    (next) => {
      usePlaybackStore.setState((state) => ({
        [key]:
          typeof next === "function"
            ? (next as (value: PlaybackState[K]) => PlaybackState[K])(
                state[key],
              )
            : next,
      }));
    },
    [key],
  );
  return [value, update];
}

export function showError(error: unknown) {
  usePlaybackStore.setState({
    error: error instanceof Error ? error.message : String(error),
  });
}

// A user action: buttons that check `busy` stay disabled until it finishes; failures show as the page error.
export async function runAction(name: string, work: () => Promise<void>) {
  usePlaybackStore.setState({ busy: name, error: "" });
  try {
    await work();
  } catch (error) {
    showError(error);
  } finally {
    usePlaybackStore.setState({ busy: "" });
  }
}
