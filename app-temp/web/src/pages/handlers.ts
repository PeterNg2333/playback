import { useEffect, useRef, useState } from "react";
import {
  AnswerSchema,
  CaptureStatusSchema,
  GroupNameSchema,
  GroupSchema,
  HealthSchema,
  QuestionSchema,
  SessionSchema,
  SessionSummarySchema,
  SessionTitleSchema,
} from "../types/api";
import type { Evidence, TermCandidate, Session } from "../types/api";
import { api, askStream } from "./api";
import { usePlaybackField, usePlaybackStore } from "./store";
import { useAudioPlayback } from "./useAudioPlayback";

export function usePlaybackController() {
  const [settingsOpen, setSettingsOpen] = usePlaybackField("settingsOpen");
  const [navOpen, setNavOpen] = usePlaybackField("navOpen");
  const [view, setView] = usePlaybackField("view");
  const [transcriptView, setTranscriptView] =
    usePlaybackField("transcriptView");
  const [recordingMode, setRecordingMode] = usePlaybackField("recordingMode");
  const [noteMode, setNoteMode] = usePlaybackField("noteMode");
  const [chatOpen, setChatOpen] = usePlaybackField("chatOpen");
  const [selection, setSelection] = usePlaybackField("selection");
  const [focusMaterialId, setFocusMaterialId] =
    usePlaybackField("focusMaterialId");
  const [session, setSession] = usePlaybackField("session");
  const [sessions, setSessions] = usePlaybackField("sessions");
  const [groups, setGroups] = usePlaybackField("groups");
  const [health, setHealth] = usePlaybackField("health");
  const [capture, setCapture] = usePlaybackField("capture");
  const [markdown, setMarkdown] = usePlaybackField("markdown");
  const [question, setQuestion] = usePlaybackField("question");
  const [answer, setAnswer] = usePlaybackField("answer");
  const [busy, setBusy] = usePlaybackField("busy");
  const [error, setError] = usePlaybackField("error");
  const [useWeb, setUseWeb] = usePlaybackField("useWeb");
  const [textDialog, setTextDialog] = usePlaybackField("textDialog");
  const [textDialogError, setTextDialogError] =
    usePlaybackField("textDialogError");
  const player = useAudioPlayback(session, setError);
  const [sourceToReveal, setSourceToReveal] = useState<{
    sessionId: string;
    evidence: Evidence;
  } | null>(null);
  const savedMarkdown = useRef("");
  const textDialogSubmitting = useRef(false);
  const refreshVersion = useRef(0);
  const questionFlight = useRef<AbortController | null>(null);
  const [answerDraft, setAnswerDraft] = useState("");
  useEffect(() => {
    document.body.dataset.view = view;
    document.body.dataset.language = session?.translationEnabled
      ? "bilingual"
      : "original";
  }, [view, session?.translationEnabled]);
  useEffect(() => {
    setSelection(null);
    setFocusMaterialId(null);
    setAnswer(null);
    setAnswerDraft("");
    questionFlight.current?.abort();
    questionFlight.current = null;
    if (usePlaybackStore.getState().busy === "ask") setBusy("");
    setError("");
  }, [session?.id]);
  async function refresh(id?: string) {
    const version = ++refreshVersion.current;
    const h = await api("/health", "GET", undefined, HealthSchema);
    setHealth(h);
    setCapture(
      await api("/capture/status", "GET", undefined, CaptureStatusSchema),
    );
    if (!h.mongo) {
      setError(
        "Local MongoDB is unavailable. Start it to create or load sessions.",
      );
      return;
    }
    const list = await api(
      "/sessions",
      "GET",
      undefined,
      SessionSummarySchema.array(),
    );
    setSessions(list);
    setGroups(await api("/groups", "GET", undefined, GroupSchema.array()));
    const chosen = [
      id,
      localStorage.getItem("playback-session"),
      list[0]?.id,
    ].find((candidate) => list.some((item) => item.id === candidate));
    if (chosen) {
      const item = await api(
        "/sessions/" + chosen,
        "GET",
        undefined,
        SessionSchema,
      );
      if (version !== refreshVersion.current) return;
      const previousSessionId = usePlaybackStore.getState().session?.id;
      const previousSavedMarkdown = savedMarkdown.current;
      setSession(item);
      savedMarkdown.current = item.noteMarkdown;
      setMarkdown((current) =>
        previousSessionId !== item.id || current === previousSavedMarkdown
          ? item.noteMarkdown
          : current,
      );
      localStorage.setItem("playback-session", chosen);
    }
  }
  async function refreshSessionSnapshot(id: string) {
    const version = refreshVersion.current;
    const item = await api("/sessions/" + id, "GET", undefined, SessionSchema);
    if (version !== refreshVersion.current || usePlaybackStore.getState().session?.id !== id) return;
    if (item.noteVersion < (usePlaybackStore.getState().session?.noteVersion ?? 0)) return;
    const previous = savedMarkdown.current;
    savedMarkdown.current = item.noteMarkdown;
    setMarkdown((current) =>
      current === previous ? item.noteMarkdown : current,
    );
    setSession(item);
  }
  useEffect(() => {
    refresh().catch((e) => setError(e.message));
    const timer = setInterval(() => {
      if (session)
        refreshSessionSnapshot(session.id).catch((e) => setError(e.message));
    }, 4000);
    return () => clearInterval(timer);
  }, [session?.id]);
  useEffect(() => {
    let active = true;
    let inFlight = false;
    const poll = async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const status = await api(
          "/capture/status",
          "GET",
          undefined,
          CaptureStatusSchema,
        );
        if (!active) return;
        const previous = usePlaybackStore.getState().capture;
        if (status.state === "recording")
          window.dispatchEvent(
            new CustomEvent("playback-capture-level", {
              detail: {
                level: Math.max(0, ...Object.values(status.levels || {})),
                streaming:
                  status.activeSegments?.some((segment) => segment.streaming) ??
                  false,
              },
            }),
          );
        const segmentKeys = (value: typeof status | null) =>
          (value?.activeSegments || [])
            .map(
              (segment) =>
                `${segment.sourceId}:${segment.startMs}:${!!segment.streaming}:${segment.interimText ?? ""}`,
            )
            .join("|");
        if (
          !previous ||
          status.state !== previous.state ||
          status.sessionId !== previous.sessionId ||
          status.error !== previous.error ||
          status.interimError !== previous.interimError ||
          status.noSoundWarning !== previous.noSoundWarning ||
          status.lastFinalizedAtMs !== previous.lastFinalizedAtMs ||
          status.recordingId !== previous.recordingId ||
          Math.floor((status.recordingElapsedMs ?? 0) / 1000) !== Math.floor((previous.recordingElapsedMs ?? 0) / 1000) ||
          segmentKeys(status) !== segmentKeys(previous) ||
          Math.floor((status.capturedThroughMs || 0) / 1000) !==
            Math.floor((previous.capturedThroughMs || 0) / 1000)
        )
          setCapture(status);
        if (
          status.sessionId &&
          status.lastFinalizedAtMs &&
          status.lastFinalizedAtMs > (previous?.lastFinalizedAtMs || 0)
        )
          await refreshSessionSnapshot(status.sessionId);
      } catch (e) {
        if (active) setError(e instanceof Error ? e.message : String(e));
      } finally {
        inFlight = false;
      }
    };
    const timer = setInterval(() => {
      void poll();
    }, 250);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, []);
  async function action(name: string, work: () => Promise<void>) {
    setBusy(name);
    setError("");
    try {
      await work();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy("");
    }
  }
  useEffect(() => {
    if (!sourceToReveal) return;
    if (sourceToReveal.sessionId !== session?.id) {
      setSourceToReveal(null);
      return;
    }
    if (view !== "transcript" || transcriptView !== "transcript") return;
    const { evidence } = sourceToReveal;
    if (evidence.kind === "material") {
      document
        .querySelector<HTMLDetailsElement>("#session-materials")
        ?.setAttribute("open", "");
    }
    const target = document.getElementById(evidence.id || "");
    target
      ?.closest<HTMLDetailsElement>(".timeline-day")
      ?.setAttribute("open", "");
    target
      ?.closest<HTMLDetailsElement>(".timeline-hour")
      ?.setAttribute("open", "");
    target?.scrollIntoView({ behavior: "smooth", block: "center" });
    setSourceToReveal(null);
  }, [sourceToReveal, session?.id, view, transcriptView]);

  function jump(evidence: Evidence) {
    if (evidence.url) {
      window.open(evidence.url, "_blank", "noopener,noreferrer");
      return;
    }
    if (!session) return;
    setView("transcript");
    setTranscriptView("transcript");
    setSourceToReveal({ sessionId: session.id, evidence });
  }
  function create(groupId: string | null = null) {
    setTextDialogError("");
    setTextDialog({ kind: "session", groupId });
  }
  async function attach() {
    if (!session) return;
    const file = await new Promise<File | null>((resolve) => {
      const input = document.createElement("input");
      input.type = "file";
      input.accept = ".txt,.md,text/plain,text/markdown";
      input.onchange = () => resolve(input.files?.[0] || null);
      input.click();
    });
    if (!file) return;
    await action("attach", async () => {
      await api(`/sessions/${session.id}/materials`, "POST", {
        name: file.name,
        text: await file.text(),
      });
      await refresh(session.id);
    });
  }
  async function ask() {
    if (!session || questionFlight.current) return;
    setAnswer(null);
    const parsed = QuestionSchema.safeParse(question);
    if (!parsed.success) {
      setError("Question must be 1–1000 characters.");
      return;
    }
    const controller = new AbortController();
    questionFlight.current = controller;
    setAnswerDraft(""); setBusy("ask"); setError("");
    const id = session.id;
    try {
      const body = {
            question: parsed.data,
            useWeb,
            transcriptId: selection?.transcriptId,
            selectedText: selection?.text,
            materialId: focusMaterialId,
            requestId: crypto.randomUUID(),
          };
      const result = health?.groundedChatFallback
        ? await askStream(id, body, controller.signal, text => {
            if (usePlaybackStore.getState().session?.id === id) setAnswerDraft(text);
          })
        : await api(`/sessions/${id}/ask`, "POST", body, AnswerSchema);
      if (usePlaybackStore.getState().session?.id !== id || controller.signal.aborted) return;
      setAnswer(result);
      setSelection(null); setFocusMaterialId(null);
    } catch (reason) {
      if (usePlaybackStore.getState().session?.id === id && !controller.signal.aborted)
        setError(reason instanceof Error ? reason.message : String(reason));
    } finally {
      if (questionFlight.current === controller) {
        questionFlight.current = null; setBusy(""); setAnswerDraft("");
      }
    }
  }
  async function retryAsr(ids: string[]) {
    if (!session) return;
    await action("retry-asr", async () => {
      for (let index = 0; index < ids.length; index += 100)
        await api(`/sessions/${session.id}/chunks/retry`, "POST", {
          chunkIds: ids.slice(index, index + 100),
        });
      await refresh(session.id);
    });
  }
  async function setLanguages(asrLanguage: Session["asrLanguage"], noteLanguage: Session["noteLanguage"], asrModel: string | null = session?.asrModel ?? null) {
    if (!session) return;
    await action("languages", async () => {
      await api(`/sessions/${session.id}/languages`, "PUT", { asrLanguage, noteLanguage, asrModel });
      await refresh(session.id);
    });
  }
  async function setTranslation(
    enabled: boolean,
    language = session?.translationLanguage || "zh-Hant",
  ) {
    if (!session) return;
    const previous = session;
    setSession({
      ...session,
      translationEnabled: enabled,
      translationLanguage: language,
    });
    await action("translation", async () => {
      try {
        await api(`/sessions/${session.id}/translation`, "PUT", {
          enabled,
          language,
        });
        await refresh(session.id);
      } catch (error) {
        setSession(previous);
        throw error;
      }
    });
  }
  async function retryTranslations() {
    if (!session) return;
    await action("translation", async () => {
      await api(`/sessions/${session.id}/translation/retry`, "POST");
      await refresh(session.id);
    });
  }
  async function reviewTerms() {
    if (!session) return;
    await action("terms", async () => {
      await api(`/sessions/${session.id}/terms/review`, "POST");
      await refresh(session.id);
      setSettingsOpen(false);
    });
  }
  function captureSelection() {
    const selected = window.getSelection();
    const text = selected?.toString().trim();
    const node = selected?.anchorNode?.parentElement;
    const row = node?.closest(".transcript-row");
    const transcript = session?.transcripts.find(
      (entry) => entry.id === row?.id,
    );
    if (text && text.length <= 1000 && transcript && (transcript.displayOriginal ?? transcript.original).includes(text))
      setSelection({
        transcriptId: transcript.id,
        text,
        startMs: transcript.startMs,
        endMs: transcript.endMs,
      });
  }
  function askTerm(candidate: TermCandidate, transcriptId?: string) {
    setQuestion(`Explain ${candidate.text} using the cited session sources.`);
    const transcript =
      session?.transcripts.find((entry) => entry.id === transcriptId) ||
      session?.transcripts.find((entry) =>
        candidate.transcriptIds.includes(entry.id),
      );
    const matched = (transcript?.displayOriginal ?? transcript?.original)?.match(
      new RegExp(candidate.text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "i"),
    )?.[0];
    setSelection(
      transcript && matched
        ? {
            transcriptId: transcript.id,
            text: matched,
            startMs: transcript.startMs,
            endMs: transcript.endMs,
          }
        : null,
    );
    setFocusMaterialId(candidate.materialIds[0] || null);
    setChatOpen(true);
  }
  async function record(command: "start" | "stop" | "pause" | "resume") {
    if (command === "start" && !session) return;
    await action("capture", async () => {
      if (command === "start" && !health?.recordingSourceSelection)
        throw new Error("Restart the API to record with the selected audio source");
      const status = await api(
        `/capture/${command}`,
        "POST",
        command === "start"
          ? {
              sessionId: session!.id,
              sourceMode: recordingMode,
            }
          : undefined,
        CaptureStatusSchema,
      );
      setCapture(status);
      await refresh(session?.id);
    });
  }
  function createGroup() {
    setTextDialogError("");
    setTextDialog({ kind: "group" });
  }
  function renameGroup(id: string, current: string) {
    setTextDialogError("");
    setTextDialog({ kind: "rename-group", id, current });
  }
  function closeTextDialog() {
    setTextDialog(null);
    setTextDialogError("");
  }
  async function submitTextDialog(value: string) {
    if (!textDialog || textDialogSubmitting.current) return;
    const schema =
      textDialog.kind === "session" ? SessionTitleSchema : GroupNameSchema;
    const parsed = schema.safeParse(value);
    if (!parsed.success) {
      setTextDialogError(
        textDialog.kind === "session"
          ? "Session title must be 1–120 characters."
          : "Group name must be 1–80 characters.",
      );
      return;
    }
    if (
      textDialog.kind === "rename-group" &&
      parsed.data === textDialog.current
    ) {
      closeTextDialog();
      return;
    }
    textDialogSubmitting.current = true;
    setBusy("dialog");
    setTextDialogError("");
    try {
      if (textDialog.kind === "session") {
        const item = await api(
          "/sessions",
          "POST",
          { title: parsed.data, groupId: textDialog.groupId },
          SessionSummarySchema,
        );
        await refresh(item.id);
      } else if (textDialog.kind === "group") {
        await api("/groups", "POST", { name: parsed.data });
        await refresh(session?.id);
      } else {
        await api("/groups/" + textDialog.id, "PUT", { name: parsed.data });
        await refresh(session?.id);
      }
      closeTextDialog();
    } catch (error) {
      setTextDialogError(
        error instanceof Error ? error.message : String(error),
      );
    } finally {
      textDialogSubmitting.current = false;
      setBusy("");
    }
  }
  async function moveSession(id: string, groupId: string | null) {
    await action("group", async () => {
      await api("/sessions/" + id + "/group", "PUT", { groupId });
      await refresh(session?.id);
    });
  }
  async function deleteGroup(id: string) {
    let deleted = false;
    await action("group", async () => {
      await api("/groups/" + id, "DELETE");
      deleted = true;
      await refresh(session?.id);
    });
    return deleted;
  }
  return {
    settingsOpen,
    setSettingsOpen,
    navOpen,
    setNavOpen,
    view,
    setView,
    transcriptView,
    setTranscriptView,
    recordingMode,
    setRecordingMode,
    noteMode,
    setNoteMode,
    chatOpen,
    setChatOpen,
    selection,
    setSelection,
    focusMaterialId,
    setFocusMaterialId,
    session,
    setSession,
    sessions,
    groups,
    health,
    capture,
    markdown,
    setMarkdown,
    question,
    setQuestion,
    answer,
    answerDraft,
    busy,
    error,
    setError,
    useWeb,
    setUseWeb,
    textDialog,
    textDialogError,
    savedMarkdown,
    refresh,
    action,
    ...player,
    jump,
    create,
    attach,
    ask,
    retryAsr,
    setTranslation,
    setLanguages,
    retryTranslations,
    reviewTerms,
    captureSelection,
    askTerm,
    record,
    createGroup,
    renameGroup,
    closeTextDialog,
    submitTextDialog,
    moveSession,
    deleteGroup,
  };
}

export type PlaybackController = ReturnType<typeof usePlaybackController>;
