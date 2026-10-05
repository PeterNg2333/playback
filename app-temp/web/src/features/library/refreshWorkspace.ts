import { isCancelledError } from "@tanstack/react-query";
import { queryClient } from "../../lib/queryClient";
import { healthQuery } from "../../lib/useHealth";
import { usePlaybackStore } from "../../lib/store";
import { captureQuery } from "../recording/captureQuery";
import { groupsQuery, sessionListQuery, sessionQuery } from "./libraryQueries";

const LAST_SESSION_KEY = "playback-session";

let latestRequest: object | undefined;

// Reloads everything the page shows — backend health, recorder status, the session list
// and one session — then shows that session. Opening a session and every write call this.
// When calls overlap, only the most recent one decides which session is shown.
export async function refreshWorkspace(sessionId?: string) {
  const request = {};
  latestRequest = request;
  usePlaybackStore.setState({ workspaceLoading: true });
  // A session read sent before a write could otherwise arrive after it with old notes.
  void queryClient.cancelQueries({ queryKey: ["session"] });
  try {
    const health = await queryClient.fetchQuery({
      ...healthQuery,
      staleTime: 0,
    });
    await queryClient.fetchQuery({ ...captureQuery, staleTime: 0 });
    if (!health.mongo) {
      usePlaybackStore.setState({
        error:
          "Local MongoDB is unavailable. Start it to create or load sessions.",
      });
      return;
    }
    const sessions = await queryClient.fetchQuery({
      ...sessionListQuery,
      staleTime: 0,
    });
    await queryClient.fetchQuery({ ...groupsQuery, staleTime: 0 });
    const chosen = [
      sessionId,
      localStorage.getItem(LAST_SESSION_KEY),
      sessions[0]?.id,
    ].find((candidate) => sessions.some((item) => item.id === candidate));
    if (!chosen) {
      if (request === latestRequest) {
        localStorage.removeItem(LAST_SESSION_KEY);
        usePlaybackStore.setState({ selectedSessionId: null });
      }
      return;
    }
    await queryClient.fetchQuery({ ...sessionQuery(chosen), staleTime: 0 });
    if (request !== latestRequest) return;
    localStorage.setItem(LAST_SESSION_KEY, chosen);
    // An error from the previous session no longer applies once another one is shown.
    usePlaybackStore.setState((state) =>
      state.selectedSessionId === chosen
        ? {}
        : { selectedSessionId: chosen, error: "" },
    );
  } catch (error) {
    if (request === latestRequest && !isCancelledError(error)) throw error;
  } finally {
    if (request === latestRequest)
      usePlaybackStore.setState({ workspaceLoading: false });
  }
}
