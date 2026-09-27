import { useEffect, useRef } from "react";
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
import type { Evidence, TermCandidate } from "../types/api";
import { api } from "./api";
import { usePlaybackField, usePlaybackStore } from "./store";
import { useAudioPlayback } from "./useAudioPlayback";

export function usePlaybackController() {
  const [settingsOpen, setSettingsOpen] = usePlaybackField("settingsOpen");
  const [navOpen, setNavOpen] = usePlaybackField("navOpen");
  const [view, setView] = usePlaybackField("view");
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
  const savedMarkdown = useRef("");
  const textDialogSubmitting = useRef(false);
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
  }, [session?.id]);
  async function refresh(id?: string) {
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
    const item = await api("/sessions/" + id, "GET", undefined, SessionSchema);
    if (usePlaybackStore.getState().session?.id !== id) return;
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
        const status = await api("/capture/status", "GET", undefined, CaptureStatusSchema);
        if (!active) return;
        const previous = usePlaybackStore.getState().capture;
        const segmentKeys = (value: typeof status | null) =>
          (value?.activeSegments || []).map((segment) => `${segment.sourceId}:${segment.startMs}`).join("|");
        if (!previous || status.state !== previous.state ||
            status.sessionId !== previous.sessionId ||
            status.error !== previous.error ||
            status.noSoundWarning !== previous.noSoundWarning ||
            status.lastFinalizedAtMs !== previous.lastFinalizedAtMs ||
            segmentKeys(status) !== segmentKeys(previous))
          setCapture(status);
        if (status.sessionId && status.lastFinalizedAtMs &&
            status.lastFinalizedAtMs > (previous?.lastFinalizedAtMs || 0))
          await refreshSessionSnapshot(status.sessionId);
      } catch (e) {
        if (active) setError(e instanceof Error ? e.message : String(e));
      } finally { inFlight = false; }
    };
    const timer = setInterval(() => { void poll(); }, 500);
    return () => { active = false; clearInterval(timer); };
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
  function jump(evidence: Evidence) {
    if (evidence.url) {
      window.open(evidence.url, "_blank", "noopener,noreferrer");
      return;
    }
    setView("transcript");
    if (evidence.kind === "material") {
      document
        .querySelector<HTMLDetailsElement>("#session-materials")
        ?.setAttribute("open", "");
    }
    setTimeout(
      () => {
        const target = document.getElementById(evidence.id || "");
        target?.closest<HTMLDetailsElement>(".timeline-day")?.setAttribute("open", "");
        target?.closest<HTMLDetailsElement>(".timeline-hour")?.setAttribute("open", "");
        target?.scrollIntoView({ behavior: "smooth", block: "center" });
      },
      30,
    );
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
    if (!session) return;
    setAnswer(null);
    const parsed = QuestionSchema.safeParse(question);
    if (!parsed.success) {
      setError("Question must be 1–1000 characters.");
      return;
    }
    await action("ask", async () => {
      setAnswer(
        await api(
          `/sessions/${session.id}/ask`,
          "POST",
          {
            question: parsed.data,
            useWeb,
            transcriptId: selection?.transcriptId,
            selectedText: selection?.text,
            materialId: focusMaterialId,
          },
          AnswerSchema,
        ),
      );
      setQuestion("");
      setSelection(null);
      setFocusMaterialId(null);
    });
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
  function captureSelection() {
    const selected = window.getSelection();
    const text = selected?.toString().trim();
    const node = selected?.anchorNode?.parentElement;
    const row = node?.closest(".transcript-row");
    const transcript = session?.transcripts.find(
      (entry) => entry.id === row?.id,
    );
    if (text && text.length <= 1000 && transcript?.original.includes(text))
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
    const matched = transcript?.original.match(
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
      const status = await api(
        `/capture/${command}`,
        "POST",
        command === "start" ? { sessionId: session!.id } : undefined,
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
