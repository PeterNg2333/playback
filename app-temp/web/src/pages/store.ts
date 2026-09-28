import type { Dispatch, SetStateAction } from "react";
import { create } from "zustand";
import type {
  Answer,
  CaptureStatus,
  Group,
  Health,
  RecordingMode,
  Session,
  SessionSummary,
} from "../types/api";

export type View = "notes" | "transcript";
export type NoteMode = "preview" | "markdown";
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
  settingsOpen: boolean;
  navOpen: boolean;
  view: View;
  transcriptView: "transcript" | "activity";
  recordingMode: RecordingMode;
  noteMode: NoteMode;
  chatOpen: boolean;
  selection: Selection | null;
  focusMaterialId: string | null;
  session: Session | null;
  sessions: SessionSummary[];
  groups: Group[];
  health: Health | null;
  capture: CaptureStatus | null;
  markdown: string;
  question: string;
  answer: Answer | null;
  busy: string;
  error: string;
  useWeb: boolean;
  textDialog: TextDialogState | null;
  textDialogError: string;
};

export const usePlaybackStore = create<PlaybackState>(() => ({
  settingsOpen: false,
  navOpen: false,
  view: "transcript",
  transcriptView: "transcript",
  recordingMode: "both",
  noteMode: "preview",
  chatOpen: false,
  selection: null,
  focusMaterialId: null,
  session: null,
  sessions: [],
  groups: [],
  health: null,
  capture: null,
  markdown: "",
  question: "",
  answer: null,
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
  const update: Dispatch<SetStateAction<PlaybackState[K]>> = (next) => {
    usePlaybackStore.setState((state) => ({
      [key]:
        typeof next === "function"
          ? (next as (value: PlaybackState[K]) => PlaybackState[K])(state[key])
          : next,
    }));
  };
  return [value, update];
}
