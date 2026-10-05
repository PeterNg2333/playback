import { useQuery } from "@tanstack/react-query";
import { queryClient } from "../../lib/queryClient";
import { useHealth } from "../../lib/useHealth";
import { api } from "../../lib/backend/client";
import { runAction, usePlaybackStore } from "../../lib/store";
import {
  GroupNameSchema,
  SessionSummarySchema,
  SessionTitleSchema,
  type Group,
  type SessionSummary,
} from "../../lib/backend/schemas";
import { groupsQuery, sessionListQuery } from "./libraryQueries";
import { refreshWorkspace } from "./refreshWorkspace";

const noSessions: SessionSummary[] = [];
const noGroups: Group[] = [];

// The sessions and groups listed in the sidebar. They exist only while MongoDB is available.
export function useLibrary() {
  const enabled = useHealth()?.mongo === true;
  const sessions =
    useQuery({ ...sessionListQuery, enabled }).data ?? noSessions;
  const groups = useQuery({ ...groupsQuery, enabled }).data ?? noGroups;
  return { sessions, groups };
}

const shownSessionId = () =>
  usePlaybackStore.getState().selectedSessionId ?? undefined;

export function openSession(id: string) {
  return runAction("load", () => refreshWorkspace(id));
}

export function moveSession(id: string, groupId: string | null) {
  return runAction("group", async () => {
    await api.put("/sessions/" + id + "/group", { groupId });
    await refreshWorkspace(shownSessionId());
  });
}

// Resolves true when the group was deleted; its sessions move back to Sessions.
export async function deleteGroup(id: string) {
  let deleted = false;
  await runAction("group", async () => {
    await api.delete("/groups/" + id);
    deleted = true;
    await refreshWorkspace(shownSessionId());
  });
  return deleted;
}

export async function deleteSession(id: string) {
  let deleted = false;
  await runAction("delete-session", async () => {
    await api.delete("/sessions/" + id);
    deleted = true;
    await queryClient.cancelQueries({ queryKey: ["session", id] });
    if (shownSessionId() === id)
      usePlaybackStore.setState({
        selectedSessionId: null,
        selection: null,
        question: "",
      });
    queryClient.removeQueries({ queryKey: ["session", id] });
    await refreshWorkspace(shownSessionId());
  });
  return deleted;
}

export function askForSessionName(groupId: string | null = null) {
  usePlaybackStore.setState({
    textDialog: { kind: "session", groupId },
    textDialogError: "",
  });
}

export function askForGroupName() {
  usePlaybackStore.setState({
    textDialog: { kind: "group" },
    textDialogError: "",
  });
}

export function askToRenameGroup(id: string, current: string) {
  usePlaybackStore.setState({
    textDialog: { kind: "rename-group", id, current },
    textDialogError: "",
  });
}

export function closeNameDialog() {
  usePlaybackStore.setState({ textDialog: null, textDialogError: "" });
}

// Creates a session or group, or renames a group, with the name typed in the dialog.
export async function submitName(value: string) {
  const { textDialog, busy } = usePlaybackStore.getState();
  if (!textDialog || busy === "dialog") return;
  const schema =
    textDialog.kind === "session" ? SessionTitleSchema : GroupNameSchema;
  const parsed = schema.safeParse(value);
  if (!parsed.success) {
    usePlaybackStore.setState({
      textDialogError:
        textDialog.kind === "session"
          ? "Session title must be 1–120 characters."
          : "Group name must be 1–80 characters.",
    });
    return;
  }
  if (
    textDialog.kind === "rename-group" &&
    parsed.data === textDialog.current
  ) {
    closeNameDialog();
    return;
  }
  usePlaybackStore.setState({ busy: "dialog", textDialogError: "" });
  try {
    if (textDialog.kind === "session") {
      const item = await api.post(
        "/sessions",
        { title: parsed.data, groupId: textDialog.groupId },
        SessionSummarySchema,
      );
      await refreshWorkspace(item.id);
    } else if (textDialog.kind === "group") {
      await api.post("/groups", { name: parsed.data });
      await refreshWorkspace(shownSessionId());
    } else {
      await api.put("/groups/" + textDialog.id, { name: parsed.data });
      await refreshWorkspace(shownSessionId());
    }
    closeNameDialog();
  } catch (error) {
    usePlaybackStore.setState({
      textDialogError: error instanceof Error ? error.message : String(error),
    });
  } finally {
    usePlaybackStore.setState({ busy: "" });
  }
}
