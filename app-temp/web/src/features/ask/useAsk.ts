import { useEffect, useRef, useState } from "react";
import { useHealth } from "../../lib/useHealth";
import { api, askStream } from "../../lib/backend/client";
import { runAction, showError, usePlaybackStore } from "../../lib/store";
import {
  AnswerSchema,
  QuestionSchema,
  type Answer,
  type Session,
} from "../../lib/backend/schemas";
import { useChatConversations } from "./useChatConversations";

// Asks the selected session a question and keeps the answer, streaming draft and
// saved conversations. Showing another session cancels the question in progress.
export function useAsk(session: Session | null) {
  const health = useHealth();
  const conversations = useChatConversations(
    session?.id,
    !!health?.chatConversations,
  );
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [answerDraft, setAnswerDraft] = useState("");
  const [sentQuestion, setSentQuestion] = useState("");
  const pendingQuestion = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    pendingQuestion.current?.abort();
    pendingQuestion.current = undefined;
    setAnswer(null);
    setAnswerDraft("");
    setSentQuestion("");
    usePlaybackStore.setState((state) => ({
      selection: null,
      focusMaterialId: null,
      question: "",
      busy: state.busy === "ask" ? "" : state.busy,
    }));
  }, [session?.id]);

  async function ask() {
    if (!session || pendingQuestion.current || conversations.loading) return;
    const { question, useWeb, selection, focusMaterialId, busy } =
      usePlaybackStore.getState();
    if (busy) return;
    setAnswer(null);
    const parsed = QuestionSchema.safeParse(question);
    if (!parsed.success) {
      showError("Question must be 1–1000 characters.");
      return;
    }
    const controller = new AbortController();
    pendingQuestion.current = controller;
    setSentQuestion(parsed.data);
    setAnswerDraft("");
    usePlaybackStore.setState({ busy: "ask", error: "", question: "" });
    try {
      const conversation = health?.chatConversations
        ? (conversations.current ?? (await conversations.create()))
        : undefined;
      if (controller.signal.aborted) return;
      const body = {
        question: parsed.data,
        useWeb,
        transcriptId: selection?.transcriptId,
        selectedText: selection?.text,
        materialId: focusMaterialId,
        requestId: crypto.randomUUID(),
        conversationId: conversation?.id,
      };
      const result = health?.groundedChatFallback
        ? await askStream(session.id, body, controller.signal, (text) => {
            if (!controller.signal.aborted) setAnswerDraft(text);
          })
        : await api.post(`/sessions/${session.id}/ask`, body, AnswerSchema);
      if (controller.signal.aborted) return;
      setAnswer(result);
      if (conversation) {
        try {
          await conversations.reload(conversation.id);
        } catch {
          if (!controller.signal.aborted)
            showError(
              "The answer is available, but conversation history could not be refreshed. Reopen it after the backend reconnects.",
            );
        }
      }
      if (controller.signal.aborted) return;
      usePlaybackStore.setState({ selection: null, focusMaterialId: null });
    } catch (reason) {
      if (!controller.signal.aborted) {
        showError(reason);
        // Restore a failed send only if the user has not started their next question.
        usePlaybackStore.setState((state) => ({
          question: state.question || question,
        }));
        setSentQuestion("");
      }
    } finally {
      if (pendingQuestion.current === controller) {
        pendingQuestion.current = undefined;
        usePlaybackStore.setState({ busy: "" });
        setAnswerDraft("");
      }
    }
  }

  function clearQuestion() {
    setAnswer(null);
    setAnswerDraft("");
    setSentQuestion("");
    usePlaybackStore.setState({
      question: "",
      selection: null,
      focusMaterialId: null,
    });
  }

  function newConversation() {
    return runAction("conversation", async () => {
      conversations.startNew();
      clearQuestion();
    });
  }

  function selectConversation(id: string) {
    return runAction("conversation", async () => {
      await conversations.select(id);
      clearQuestion();
    });
  }

  return {
    conversations,
    answer,
    answerDraft,
    sentQuestion,
    ask,
    newConversation,
    selectConversation,
  };
}
