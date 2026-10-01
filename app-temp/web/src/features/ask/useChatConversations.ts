import { queryOptions, useIsFetching, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queryClient } from "../../lib/queryClient";
import { api } from "../../pages/api";
import {
  ConversationSchema,
  ConversationSummarySchema,
  type Conversation,
  type ConversationSummary,
} from "../../types/api";

const noConversations: ConversationSummary[] = [];
const savedConversationKey = (sessionId: string) =>
  `playback-conversation:${sessionId}`;

const conversationListQuery = (sessionId: string) =>
  queryOptions({
    queryKey: ["conversations", sessionId],
    queryFn: () =>
      api(
        `/sessions/${sessionId}/conversations`,
        "GET",
        undefined,
        ConversationSummarySchema.array(),
      ),
  });

// A conversation changes only when this page saves an answer to it, which reloads it.
const conversationQuery = (sessionId: string, id: string) =>
  queryOptions({
    queryKey: ["conversations", sessionId, id],
    queryFn: () =>
      api(
        `/sessions/${sessionId}/conversations/${id}`,
        "GET",
        undefined,
        ConversationSchema,
      ),
    staleTime: Infinity,
  });

// The saved Ask conversations of one session and the one being shown. The shown
// conversation is remembered per session in this browser.
export function useChatConversations(
  sessionId: string | undefined,
  enabled: boolean,
) {
  const [chosen, setChosen] = useState<{ sessionId: string; id: string }>();
  const listQuery = useQuery({
    ...conversationListQuery(sessionId ?? ""),
    enabled: !!sessionId && enabled,
  });
  const list = listQuery.data ?? noConversations;
  const savedId = sessionId
    ? localStorage.getItem(savedConversationKey(sessionId))
    : null;
  const currentId =
    chosen && chosen.sessionId === sessionId
      ? chosen.id
      : list.find((item) => item.id === savedId)?.id;
  const currentQuery = useQuery({
    ...conversationQuery(sessionId ?? "", currentId ?? ""),
    enabled: !!sessionId && enabled && !!currentId,
  });
  const fetching = useIsFetching({ queryKey: ["conversations", sessionId] });
  const loading =
    !!sessionId &&
    enabled &&
    (listQuery.isPending ||
      (!!currentId && currentQuery.isPending) ||
      !!fetching);

  function show(forSession: string, id: string) {
    localStorage.setItem(savedConversationKey(forSession), id);
    setChosen({ sessionId: forSession, id });
  }

  async function select(id: string) {
    if (!sessionId) return;
    const conversation = await queryClient.fetchQuery({
      ...conversationQuery(sessionId, id),
      staleTime: 0,
    });
    show(sessionId, id);
    return conversation;
  }

  async function create() {
    if (!sessionId || !enabled) return;
    const item = await api(
      `/sessions/${sessionId}/conversations`,
      "POST",
      undefined,
      ConversationSummarySchema,
    );
    const conversation: Conversation = { ...item, turns: [] };
    queryClient.setQueryData(
      conversationListQuery(sessionId).queryKey,
      (previous = []) => [item, ...previous],
    );
    queryClient.setQueryData(
      conversationQuery(sessionId, item.id).queryKey,
      conversation,
    );
    show(sessionId, item.id);
    return conversation;
  }

  // Reads a conversation again after a new answer was saved to it.
  async function reload(id: string) {
    if (!sessionId) return;
    const conversation = await queryClient.fetchQuery({
      ...conversationQuery(sessionId, id),
      staleTime: 0,
    });
    show(sessionId, id);
    queryClient.setQueryData(
      conversationListQuery(sessionId).queryKey,
      (previous = []) =>
        previous.map((item) => (item.id === id ? conversation : item)),
    );
  }

  return {
    list: enabled ? list : noConversations,
    current: enabled ? currentQuery.data : undefined,
    loading,
    error: (listQuery.error ?? currentQuery.error)?.message,
    select,
    create,
    reload,
  };
}

export type ChatConversations = ReturnType<typeof useChatConversations>;
