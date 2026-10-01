import { useEffect, useRef, useState } from "react";
import {
  ConversationSchema,
  ConversationSummarySchema,
  type Conversation,
  type ConversationSummary,
} from "../types/api";
import { api } from "./api";

export function useChatConversations(sessionId?: string, enabled = false) {
  const [state, setState] = useState<{
    sessionId?: string;
    list: ConversationSummary[];
    current?: Conversation;
    loading: boolean;
    error?: string;
  }>({ list: [], loading: false });
  const generation = useRef(0);
  const activeSession = useRef(sessionId);
  activeSession.current = sessionId;
  useEffect(() => {
    const version = ++generation.current;
    setState({ sessionId, list: [], loading: !!sessionId && enabled });
    if (!sessionId || !enabled) return;
    const load = async () => {
      const list = await api(
        `/sessions/${sessionId}/conversations`,
        "GET",
        undefined,
        ConversationSummarySchema.array(),
      );
      const saved = localStorage.getItem(`playback-conversation:${sessionId}`);
      const chosen = list.find((item) => item.id === saved);
      const current = chosen
        ? await api(
            `/sessions/${sessionId}/conversations/${chosen.id}`,
            "GET",
            undefined,
            ConversationSchema,
          )
        : undefined;
      if (version === generation.current)
        setState({ sessionId, list, current, loading: false });
    };
    void load().catch((reason) => {
      if (version === generation.current)
        setState({
          sessionId,
          list: [],
          loading: false,
          error: reason.message,
        });
    });
    return () => {
      generation.current++;
    };
  }, [sessionId, enabled]);
  async function select(id: string) {
    if (!sessionId) return;
    const version = ++generation.current;
    setState((previous) => ({ ...previous, loading: true, error: undefined }));
    try {
      const current = await api(
        `/sessions/${sessionId}/conversations/${id}`,
        "GET",
        undefined,
        ConversationSchema,
      );
      if (version !== generation.current || activeSession.current !== sessionId)
        return;
      localStorage.setItem(`playback-conversation:${sessionId}`, id);
      setState((previous) => ({
        ...previous,
        sessionId,
        current,
        loading: false,
      }));
      return current;
    } catch (reason) {
      if (version === generation.current)
        setState((previous) => ({ ...previous, loading: false }));
      throw reason;
    }
  }
  async function create() {
    if (!sessionId || !enabled) return;
    const version = ++generation.current;
    const item = await api(
      `/sessions/${sessionId}/conversations`,
      "POST",
      undefined,
      ConversationSummarySchema,
    );
    if (version !== generation.current || activeSession.current !== sessionId)
      return;
    const current: Conversation = { ...item, turns: [] };
    localStorage.setItem(`playback-conversation:${sessionId}`, item.id);
    setState((previous) => ({
      sessionId,
      list: [item, ...previous.list],
      current,
      loading: false,
    }));
    return current;
  }
  async function reload(id: string) {
    const current = await select(id);
    if (current)
      setState((previous) => ({
        ...previous,
        list: previous.list.map((item) => (item.id === id ? current : item)),
      }));
  }
  return {
    ...(state.sessionId === sessionId ? state : { list: [], loading: true }),
    select,
    create,
    reload,
  };
}
