import { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeSanitize from "rehype-sanitize";
import mermaid from "mermaid";
import DOMPurify from "dompurify";
import "./style.css";
import "./extras.css";

type Transcript = {
  id: string;
  sourceId: string;
  startMs: number;
  endMs: number;
  recordedAt?: string;
  original: string;
  translation?: string;
  translationLanguage?: string;
  translationStatus?: string;
  translationAttempts?: number;
  translationError?: string;
  recognitionStatus?: string;
  noteStatus?: string;
  revision?: string;
  uncertain: boolean;
};
type Material = { id: string; name: string; text: string };
type TermCandidate = { text: string; transcriptIds: string[]; materialIds: string[] };
type Chunk = {
  id: string;
  sourceId: string;
  sequence: number;
  startMs: number;
  endMs: number;
  status: string;
  error?: string;
};
type Session = {
  id: string;
  title: string;
  groupId?: string | null;
  createdAt: string;
  noteMarkdown: string;
  noteVersion: number;
  translationEnabled: boolean;
  translationLanguage: string;
  externalProcessingConsent: boolean;
  materials: Material[];
  transcripts: Transcript[];
  chunks: Chunk[];
  terms: TermCandidate[];
  currentNote?: { author: string; basedOnVersion?: number; inputHash?: string; transcriptIds: string[]; materialIds: string[]; sourceFrom?: string; sourceThrough?: string };
};
type Evidence = {
  kind: string;
  id?: string;
  url?: string;
  title?: string;
  label?: string;
  startIndex?: number;
  endIndex?: number;
};
type Answer = {
  answer: string;
  webAnswer?: string;
  evidence: Evidence[];
  inference: boolean;
};
type Health = { mongo: boolean; gemini: boolean; jev: boolean; automaticAsr?: boolean };
type CaptureStatus = {
  state: "idle" | "recording" | "paused";
  sessionId?: string;
  bytes: Record<string, number>;
  autoAsr?: boolean;
  noSoundWarning?: boolean;
  error?: string;
};
async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  const response = await fetch("/api" + path, {
    method,
    headers: {
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error || `HTTP ${response.status}`);
  }
  return response.json();
}

mermaid.initialize({
  startOnLoad: false,
  securityLevel: "strict",
  theme: "neutral",
  htmlLabels: false,
});
function Diagram({ source }: { source: string }) {
  const [svg, setSvg] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    mermaid
      .render("diagram-" + crypto.randomUUID().replaceAll("-", ""), source)
      .then((result) => {
        if (active)
          setSvg(
            DOMPurify.sanitize(result.svg, {
              USE_PROFILES: { html: true, svg: true, svgFilters: true },
            }),
          );
      })
      .catch(() => {
        if (active) setError("Diagram syntax needs review.");
      });
    return () => {
      active = false;
    };
  }, [source]);
  return (
    <figure className="flowchart">
      <figcaption>Flowchart</figcaption>
      {error ? (
        <p role="alert">{error}</p>
      ) : (
        <div
          className="flowchart-scroll"
          aria-label="Rendered flowchart"
          dangerouslySetInnerHTML={{ __html: svg }}
        />
      )}
    </figure>
  );
}

function Markdown({ value }: { value: string }) {
  return (
    <div className="markdown-preview">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSanitize]}
        components={{
          code(props) {
            const language = /language-(\w+)/.exec(props.className || "")?.[1];
            const source = String(props.children).replace(/\n$/, "");
            return language === "mermaid" || language === "flowchart" ? (
              <Diagram source={source} />
            ) : (
              <code>{props.children}</code>
            );
          },
          a(props) {
            return (
              <a href={props.href} target="_blank" rel="noopener noreferrer">
                {props.children}
              </a>
            );
          },
        }}
      >
        {value}
      </ReactMarkdown>
    </div>
  );
}

function Icon({ name }: { name: string }) {
  const path =
    name === "record" ? (
      <circle cx="12" cy="12" r="6" fill="currentColor" stroke="none" />
    ) : name === "pause" ? (
      <><path d="M8 5v14M16 5v14" /></>
    ) : name === "stop" ? (
      <rect x="6" y="6" width="12" height="12" rx="2" fill="currentColor" stroke="none" />
    ) : name === "settings" ? (
      <><circle cx="12" cy="12" r="3" /><path d="M12 2v3m0 14v3M2 12h3m14 0h3M4.9 4.9 7 7m10 10 2.1 2.1M19.1 4.9 17 7M7 17l-2.1 2.1" /></>
    ) : name === "menu" ? (
      <path d="M4 7h16M4 12h16M4 17h16" />
    ) : name === "wave" ? (
      <path d="M3 12c2.2 0 2.2-5 4.5-5S9.8 17 12 17s2.2-10 4.5-10S18.8 12 21 12" />
    ) : name === "loop" ? (
      <>
        <path d="M19 7a8 8 0 1 0 1 8M19 3v4h-4" />
        <path d="m10 8 6 4-6 4" />
      </>
    ) : (
      <path d="M3 12h3l2-5 3 10 2-7 2 3h6" />
    );
  return (
    <svg
      viewBox="0 0 24 24"
      width="22"
      height="22"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {path}
    </svg>
  );
}
const time = (ms: number) =>
  `${String(Math.floor(ms / 60000)).padStart(2, "0")}:${String(Math.floor((ms % 60000) / 1000)).padStart(2, "0")}`;
const cleanAsrText = (value: string) => value.replace(/<\|[^|>]*\|>/g, "").trim();
const chunkStatus = (status: string) => ({ silent: "Exact digital silence · ASR skipped", "asr-empty": "ASR returned no words · audio retained", "asr-error": "Recognition failed · retry queued", "awaiting-consent": "Audio saved · waiting for external processing consent", transcribing: "Recognizing speech", transcribed: "Transcript ready" } as Record<string, string>)[status] || status;

function transcriptDays(session: Session) {
  const days = new Map<string, Map<string, Transcript[]>>();
  const ordered = session.transcripts.map((entry) => ({
    entry,
    at: entry.recordedAt
      ? new Date(entry.recordedAt)
      : new Date(new Date(session.createdAt).getTime() + entry.startMs),
  })).sort((first, second) => first.at.getTime() - second.at.getTime());
  for (const { entry, at } of ordered) {
    const day = new Intl.DateTimeFormat(undefined, { weekday: "long", year: "numeric", month: "long", day: "numeric" }).format(at);
    const hour = String(at.getHours()).padStart(2, "0") + ":00";
    if (!days.has(day)) days.set(day, new Map());
    const hours = days.get(day)!;
    if (!hours.has(hour)) hours.set(hour, []);
    hours.get(hour)!.push(entry);
  }
  return days;
}

function App() {
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  const [view, setView] = useState<"notes" | "transcript" | "sources">(
    "transcript",
  );
  const [noteMode, setNoteMode] = useState<"preview" | "markdown">("preview");
  const [chatOpen, setChatOpen] = useState(false);
  const [selection, setSelection] = useState<{ transcriptId: string; text: string; startMs: number; endMs: number } | null>(null);
  const [focusMaterialId, setFocusMaterialId] = useState<string | null>(null);
  const [session, setSession] = useState<Session | null>(null),
    [sessions, setSessions] = useState<{ id: string; title: string; groupId?: string | null }[]>([]),
    [groups, setGroups] = useState<{ id: string; name: string }[]>([]);
  const [health, setHealth] = useState<Health | null>(null),
    [capture, setCapture] = useState<CaptureStatus | null>(null),
    [markdown, setMarkdown] = useState(""),
    [question, setQuestion] = useState(""),
    [answer, setAnswer] = useState<Answer | null>(null);
  const [busy, setBusy] = useState(""),
    [error, setError] = useState("");
  const [useWeb, setUseWeb] = useState(false);
  const audio = useRef<HTMLAudioElement>(null);
  const savedMarkdown = useRef("");
  useEffect(() => {
    document.body.dataset.view = view;
    document.body.dataset.language = session?.translationEnabled ? "bilingual" : "original";
  }, [view, session?.translationEnabled]);
  useEffect(() => { setSelection(null); setFocusMaterialId(null); }, [session?.id]);
  async function refresh(id?: string) {
    const h = await api<Health>("/health");
    setHealth(h);
    setCapture(await api<CaptureStatus>("/capture/status"));
    if (!h.mongo) {
      setError(
        "Local MongoDB is unavailable. Start it to create or load sessions.",
      );
      return;
    }
    const list = await api<{ id: string; title: string; groupId?: string | null }[]>("/sessions");
    setSessions(list);
    setGroups(await api<{ id: string; name: string }[]>("/groups"));
    const chosen =
      [id, localStorage.getItem("playback-session"), list[0]?.id].find((candidate) =>
        list.some((item) => item.id === candidate));
    if (chosen) {
      const item = await api<Session>("/sessions/" + chosen);
      setSession(item);
      savedMarkdown.current = item.noteMarkdown;
      setMarkdown(item.noteMarkdown);
      localStorage.setItem("playback-session", chosen);
    }
  }
  useEffect(() => {
    refresh().catch((e) => setError(e.message));
    const timer = setInterval(() => {
      if (session)
        api<Session>("/sessions/" + session.id)
          .then((item) => {
            const previous = savedMarkdown.current;
            savedMarkdown.current = item.noteMarkdown;
            setMarkdown((current) => current === previous ? item.noteMarkdown : current);
            setSession(item);
          })
          .catch((e) => setError(e.message));
      api<CaptureStatus>("/capture/status")
        .then(setCapture)
        .catch((e) => setError(e.message));
    }, 4000);
    return () => clearInterval(timer);
  }, [session?.id]);
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
  function seek(entry: Transcript) {
    if (!audio.current) return;
    audio.current.src = `/api/chunks/${entry.id}/audio`;
    audio.current.currentTime = 0;
    audio.current.play().catch((e) => setError(e.message));
  }
  function jump(evidence: Evidence) {
    if (evidence.url) {
      window.open(evidence.url, "_blank", "noopener,noreferrer");
      return;
    }
    setView(evidence.kind === "material" ? "sources" : "transcript");
    setTimeout(
      () =>
        document
          .getElementById(evidence.id || "")
          ?.scrollIntoView({ behavior: "smooth", block: "center" }),
      30,
    );
  }
  async function create(groupId: string | null = null) {
    const title = prompt("Session title");
    if (!title?.trim()) return;
    await action("create", async () => {
      const item = await api<{ id: string }>("/sessions", "POST", {
        title: title.trim(),
        groupId,
      });
      await refresh(item.id);
    });
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
    if (!session || !question.trim()) return;
    await action("ask", async () => {
      setAnswer(
        await api<Answer>(`/sessions/${session.id}/ask`, "POST", {
          question: question.trim(),
          useWeb,
          webConsentConfirmed: useWeb,
          transcriptId: selection?.transcriptId,
          selectedText: selection?.text,
          materialId: focusMaterialId,
        }),
      );
      setQuestion("");
      setSelection(null);
      setFocusMaterialId(null);
    });
  }
  async function setConsent(confirmed: boolean) {
    if (!session) return;
    const previous = session;
    setSession({ ...session, externalProcessingConsent: confirmed });
    await action("consent", async () => {
      try {
        await api(`/sessions/${session.id}/consent`, "PUT", { confirmed });
        await refresh(session.id);
      } catch (error) { setSession(previous); throw error; }
    });
  }
  async function setTranslation(enabled: boolean, language = session?.translationLanguage || "zh-Hant") {
    if (!session) return;
    const previous = session;
    setSession({ ...session, translationEnabled: enabled, translationLanguage: language });
    await action("translation", async () => {
      try {
        await api(`/sessions/${session.id}/translation`, "PUT", { enabled, language, consentConfirmed: enabled });
        await refresh(session.id);
      } catch (error) { setSession(previous); throw error; }
    });
  }
  function captureSelection() {
    const selected = window.getSelection();
    const text = selected?.toString().trim();
    const node = selected?.anchorNode?.parentElement;
    const row = node?.closest(".transcript-row");
    const transcript = session?.transcripts.find((entry) => entry.id === row?.id);
    if (text && text.length <= 1000 && transcript?.original.includes(text))
      setSelection({ transcriptId: transcript.id, text, startMs: transcript.startMs, endMs: transcript.endMs });
  }
  function askTerm(candidate: TermCandidate, transcriptId?: string) {
    setQuestion(`Explain ${candidate.text} using the cited session sources.`);
    const transcript = session?.transcripts.find((entry) => entry.id === transcriptId) ||
      session?.transcripts.find((entry) => candidate.transcriptIds.includes(entry.id));
    const matched = transcript?.original.match(new RegExp(candidate.text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "i"))?.[0];
    setSelection(transcript && matched ? { transcriptId: transcript.id, text: matched, startMs: transcript.startMs, endMs: transcript.endMs } : null);
    setFocusMaterialId(candidate.materialIds[0] || null);
    setChatOpen(true);
  }
  async function record(command: "start" | "stop" | "pause" | "resume") {
    if (command === "start" && !session) return;
    if (command === "start" && !window.confirm("Before recording, confirm the lecturer's, participants' and institution's consent and applicable privacy and retention rules. Continue with local recording?")) return;
    await action("capture", async () => {
      const status = await api<CaptureStatus>(
        `/capture/${command}`,
        "POST",
        command === "start" ? { sessionId: session!.id, consentConfirmed: true } : undefined,
      );
      setCapture(status);
      await refresh(session?.id);
    });
  }
  async function createGroup() {
    const name = prompt("Group name");
    if (!name?.trim()) return;
    await action("group", async () => {
      await api("/groups", "POST", { name: name.trim() });
      await refresh(session?.id);
    });
  }
  async function renameGroup(id: string, current: string) {
    const name = prompt("Rename group", current);
    if (!name?.trim() || name.trim() === current) return;
    await action("group", async () => {
      await api("/groups/" + id, "PUT", { name: name.trim() });
      await refresh(session?.id);
    });
  }
  async function moveSession(id: string, groupId: string | null) {
    await action("group", async () => {
      await api("/sessions/" + id + "/group", "PUT", { groupId });
      await refresh(session?.id);
    });
  }
  function transcriptRow(transcript: Transcript) {
    return (
      <article className="transcript-row" id={transcript.id} key={transcript.id}>
        <button
          className="time-range"
          onClick={() => seek(transcript)}
          aria-label={`Play audio from ${time(transcript.startMs)} to ${time(transcript.endMs)}`}
        >
          {time(transcript.startMs)}–{time(transcript.endMs)}
        </button>
        <div>
          <span className="speaker">{transcript.sourceId}</span>
          <p className="original" onMouseUp={captureSelection} onKeyUp={captureSelection}>{cleanAsrText(transcript.original) || "No words returned by ASR"}</p>
          {transcript.uncertain && <span className="uncertain">Unclear · review audio</span>}
          {transcript.recognitionStatus === "asr-empty" && <span className="processing-status">ASR returned no text · audio retained</span>}
          {session?.translationEnabled && transcript.translationStatus === "completed" && transcript.translationLanguage === session.translationLanguage && transcript.translation && <p className="translation">Translation: {transcript.translation}</p>}
          {session?.translationEnabled && transcript.original && transcript.translationStatus === "failed" && <span className="processing-status">Translation failed; retry available in settings</span>}
          {transcript.revision && <p className="translation">Suggested revision: {transcript.revision}</p>}
          {session?.terms.filter((candidate) => candidate.transcriptIds.includes(transcript.id)).map((candidate) => (
            <button className="term-tag" key={candidate.text} onClick={() => askTerm(candidate, transcript.id)} title="Ask Playback with this source">{candidate.text}</button>
          ))}
        </div>
      </article>
    );
  }
  return (
    <div className="app-shell"
      data-view={view}
      data-language={session?.translationEnabled ? "bilingual" : "original"}
    >
      <a className="skip" href="#workspace">
        Skip to workspace
      </a>
      <header className="topbar">
        <div className="brand">
          <button className="nav-toggle icon-control" aria-label="Toggle sessions" aria-expanded={navOpen} onClick={() => { setSettingsOpen(false); setNavOpen(!navOpen); }}><Icon name="menu" /></button>
          <span className="brand-icon">
            <Icon name="pulse" />
          </span>
          <span className="brand-title">Playback</span>
        </div>
        <h1 className="project-name">{session?.title || "Choose a session"}</h1>
        <div className="top-actions">
          <div className="record-actions" data-state={capture?.state || "idle"}>
            {capture?.state === "idle" ? (
              <button className="record-start" disabled={!session || !!busy || !health?.mongo} onClick={() => record("start")} aria-label="Start recording"><Icon name="record" /><span>Record</span></button>
            ) : (
              <>
                <span className="record-indicator" role="status"><Icon name="record" />{capture?.state === "paused" ? "Paused" : "Recording"}</span>
                <button className="icon-control" disabled={!!busy} onClick={() => record(capture?.state === "paused" ? "resume" : "pause")} aria-label={capture?.state === "paused" ? "Resume recording" : "Pause recording"}><Icon name={capture?.state === "paused" ? "record" : "pause"} /></button>
                <button className="icon-control" disabled={!!busy} onClick={() => record("stop")} aria-label="Stop recording"><Icon name="stop" /></button>
              </>
            )}
          </div>
        </div>
      </header>
      {capture?.noSoundWarning && <div className="sound-warning" role="alert">No microphone sound detected for over a minute. Check that your microphone is enabled and connected.</div>}
      <nav className={`sidebar ${navOpen ? "is-open" : ""}`} aria-label="Sessions">
        <div className="sidebar-top">
          <span>Workspace</span>
          <button className="nav-close icon-control" aria-label="Close sessions" onClick={() => setNavOpen(false)}>×</button>
        </div>
        <button className="new-session" aria-label="New session" onClick={() => create()} disabled={!health?.mongo || !!busy}>＋ New session</button>
        <div className="sidebar-heading"><span>Groups</span><button aria-label="New group" onClick={createGroup} disabled={!health?.mongo || !!busy}>＋</button></div>
        {groups.map((group) => (
          <div className="session-group" key={group.id}>
            <div className="group-heading"><span>{group.name}</span><button aria-label={`Rename ${group.name}`} onClick={() => renameGroup(group.id, group.name)}>···</button></div>
            <button className="group-create" aria-label={`New session in ${group.name}`} onClick={() => create(group.id)} disabled={!health?.mongo || !!busy}>＋ New session</button>
            {sessions.filter((item) => item.groupId === group.id).map((item) => (
              <div key={item.id} className="session-entry"><button className="session-link" aria-current={session?.id === item.id ? "page" : undefined} onClick={() => { action("load", () => refresh(item.id)); setNavOpen(false); }}>{item.title}</button>{session?.id === item.id && <label className="session-move">Move session<select aria-label={`Move ${item.title} to group`} value={session.groupId || ""} onChange={(e) => moveSession(item.id, e.target.value || null)}><option value="">Ungrouped</option>{groups.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}</select></label>}</div>
            ))}
          </div>
        ))}
        <div className="sidebar-heading recent-heading">Ungrouped sessions</div>
        {sessions.filter((item) => !item.groupId || !groups.some((group) => group.id === item.groupId)).map((item) => (
          <div key={item.id} className="session-entry"><button className="session-link" aria-current={session?.id === item.id ? "page" : undefined} onClick={() => { action("load", () => refresh(item.id)); setNavOpen(false); }}>{item.title}</button>{session?.id === item.id && <label className="session-move">Move session<select aria-label={`Move ${item.title} to group`} value={session.groupId || ""} onChange={(e) => moveSession(item.id, e.target.value || null)}><option value="">Ungrouped</option>{groups.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}</select></label>}</div>
        ))}
      </nav>
      <main className="workspace" id="workspace">
        <nav className="mobile-nav" aria-label="Workspace views">
          {(["notes", "transcript"] as const).map((v) => (
            <button
              key={v}
              aria-current={view === v ? "page" : undefined}
              onClick={() => setView(v)}
            >
              {v[0].toUpperCase() + v.slice(1)}
            </button>
          ))}
        </nav>
        <section className="panel notes-panel">
          <div className="panel-head">
            <div className="note-title"><h2>Lecture notes</h2><span>v{session?.noteVersion || 0}</span></div>
            <div className="panel-actions">
              <div className="segmented">
                <button
                  aria-pressed={noteMode === "preview"}
                  onClick={() => setNoteMode("preview")}
                >
                  Preview
                </button>
                <button
                  aria-pressed={noteMode === "markdown"}
                  onClick={() => setNoteMode("markdown")}
                >
                  Edit
                </button>
              </div>
            </div>
          </div>
          <div className="notes-body">
            {noteMode === "preview" ? (
              markdown ? (
                <>
                  <Markdown value={markdown} />
                  {!!session?.currentNote && <details className="note-sources"><summary>{session.currentNote.author === "user" ? "Source lineage from earlier notes" : "Sources used for this note version"} · {session.currentNote.transcriptIds.length} audio · {session.currentNote.materialIds.length} materials</summary>
                    {session.currentNote.transcriptIds.map((id) => { const source = session.transcripts.find((entry) => entry.id === id); return source && <button className="citation" key={id} onClick={() => jump({ kind: "lecture", id })}>{time(source.startMs)}–{time(source.endMs)}</button>; })}
                    {session.currentNote.materialIds.map((id) => { const source = session.materials.find((entry) => entry.id === id); return source && <button className="citation" key={id} onClick={() => jump({ kind: "material", id })}>{source.name}</button>; })}
                  </details>}
                </>
              ) : (
                <p className="empty">
                  Notes will appear after you edit or generate them.
                </p>
              )
            ) : (
              <div className="editor-wrap">
                <label htmlFor="note-editor" className="sr-only">Editable Markdown</label>
                <textarea
                  id="note-editor"
                  className="markdown-editor"
                  value={markdown}
                  onChange={(e) => setMarkdown(e.target.value)}
                  placeholder="# Lecture notes&#10;&#10;```mermaid&#10;flowchart LR&#10;Audio --> Notes&#10;```"
                />
              </div>
            )}
          </div>
          {!!session?.transcripts.filter((entry) => entry.noteStatus === "failed").length && <p className="note-status" role="status">Notes update failed for {session.transcripts.filter((entry) => entry.noteStatus === "failed").length} transcript entries. Retry with “Revise with AI”.</p>}
          <footer className="note-footer">
                <button
                  className="save-button"
                  disabled={!session || !!busy || markdown === savedMarkdown.current}
                  onClick={() =>
                    action("save", async () => {
                      await api(`/sessions/${session!.id}/notes`, "POST", {
                        markdown,
                      });
                      await refresh(session!.id);
                    })
                  }
                >
                  Save
                </button>
            <button
              className="secondary-action"
              disabled={!session || !!busy || !health?.gemini || !session.externalProcessingConsent || markdown !== savedMarkdown.current}
              title={!session?.externalProcessingConsent ? "Confirm external processing consent in Transcript settings" : markdown !== savedMarkdown.current ? "Save your edits before revising with AI" : undefined}
              onClick={() =>
                action("generate", async () => {
                  await api(`/sessions/${session!.id}/notes/generate`, "POST");
                  await refresh(session!.id);
                })
              }
            >
              Revise with AI
            </button>
          </footer>
        </section>
        <section className="panel transcript-panel">
          <div className="panel-head">
            <h2>{view === "sources" ? "Sources" : "Transcript"}</h2>
            <div className="panel-actions">
              {view === "sources" && <button className="text-control" onClick={() => setView("transcript")}>Back to transcript</button>}
              {view === "transcript" && <button className="text-control" onClick={() => { setSettingsOpen(false); setView("sources"); }}>Sources</button>}
              {view !== "sources" && !!session?.chunks.filter((c) => !["transcribed", "asr-empty", "silent"].includes(c.status)).length && <span className="processing-status">Processing audio…</span>}
              {view !== "sources" && <div className="settings-wrap">
                <button className="icon-control" aria-label="Transcript settings" aria-expanded={settingsOpen} onClick={() => setSettingsOpen(!settingsOpen)}><Icon name="settings" /></button>
                {settingsOpen && <div className="settings-menu">
                  <strong>Transcript settings</strong>
                  <label className="settings-check"><input type="checkbox" checked={!!session?.externalProcessingConsent} disabled={!session || !!busy} onChange={(e) => setConsent(e.target.checked)} /> I confirm lecturer, participants and institution consent, and privacy and retention rules, for external processing.</label>
                  <label className="settings-check"><input type="checkbox" checked={!!session?.translationEnabled} disabled={!session?.externalProcessingConsent || !!busy} onChange={(e) => setTranslation(e.target.checked)} /> 啟用翻譯</label>
                  <label htmlFor="translation-language">目標語言</label>
                  <select id="translation-language" value={session?.translationLanguage || "zh-Hant"} disabled={!session || !!busy} onChange={(e) => setTranslation(!!session?.translationEnabled, e.target.value)}>
                    <option value="zh-Hant">繁體中文</option><option value="en">English</option><option value="ja">日本語</option><option value="ko">한국어</option>
                  </select>
                  <small>Applies to this session. Existing entries are translated in the background; originals remain unchanged.</small>
                  {session?.translationEnabled && session.transcripts.some((entry) => entry.translationStatus === "failed") && <button className="text-control" disabled={!!busy} onClick={() => action("translation", async () => { await api(`/sessions/${session!.id}/translation/retry`, "POST"); await refresh(session!.id); })}>Retry failed translations</button>}
                </div>}
              </div>}
            </div>
          </div>
          {view === "sources" ? (
            <div className="source-content">
              <button onClick={attach} disabled={!session || !!busy}>
                Attach text material
              </button>
              {session?.chunks.some((chunk) => !["transcribed", "asr-empty", "silent"].includes(chunk.status)) && (
                <p className="asr-help">Saved audio remains linked to this session. With consent, failed recognition retries in the background.</p>
              )}
              {session?.materials.map((m) => (
                <article className="source-item" id={m.id} key={m.id}>
                  <span className="source-icon">↗</span>
                  <div>
                    <strong>{m.name}</strong>
                    <p>{m.text.slice(0, 180)}</p>
                    {session.terms.filter((candidate) => candidate.materialIds.includes(m.id)).map((candidate) => <button className="term-tag" key={candidate.text} onClick={() => askTerm(candidate)}>{candidate.text}</button>)}
                  </div>
                </article>
              ))}
              {session?.chunks.map((c) => (
                <article className="source-item" key={c.id}>
                  <span className="source-icon">♫</span>
                  <div>
                    <strong>
                      {c.sourceId} · {time(c.startMs)}–{time(c.endMs)}
                    </strong>
                    <p>{chunkStatus(c.status)}</p>
                    {c.error && <p className="capture-error" role="alert">{c.error}</p>}
                    <audio
                      controls
                      preload="none"
                      src={`/api/chunks/${c.id}/audio`}
                    />
                  </div>
                </article>
              ))}
            </div>
          ) : (
            <div className="transcript-content">
              {capture?.error && <p className="capture-error" role="alert">{capture.error}</p>}
              <div className="rows">
                {session?.transcripts.length ? (
                  Array.from(transcriptDays(session).entries()).map(([day, hours]) => (
                    <details className="timeline-day" key={day} open>
                      <summary>{day}</summary>
                      {Array.from(hours.entries()).map(([hour, entries]) => (
                        <details className="timeline-hour" key={hour} open>
                          <summary>{hour} · {entries.length} entries</summary>
                          {entries.map(transcriptRow)}
                        </details>
                      ))}
                    </details>
                  ))
                ) : (
                  <p className="empty">
                    No transcript yet. Record a session to see it here.
                  </p>
                )}
              </div>
            </div>
          )}
        </section>
      </main>
      <audio ref={audio} hidden />
      {selection && !chatOpen && <button className="selection-action" onClick={() => { if (!question.trim()) setQuestion("Explain this selected passage using its source context."); setChatOpen(true); }}>Ask Playback about selection · {time(selection.startMs)}–{time(selection.endMs)}</button>}
      {error && (
        <div className="global-error" role="alert">
          {error}
          <button aria-label="Dismiss error" onClick={() => setError("")}>
            ×
          </button>
        </div>
      )}
      {!chatOpen ? (
        <button className="floating" onClick={() => setChatOpen(true)}>
          ◇ Ask Playback
        </button>
      ) : (
        <section className="chat open" aria-label="Ask Playback">
          <div className="chat-head">
            <div>
              <strong>Ask Playback</strong>
              <small>Private · processed session content</small>
            </div>
            <button
              className="icon-button"
              onClick={() => setChatOpen(false)}
              aria-label="Close chat"
            >
              ×
            </button>
          </div>
          <div className="chat-scroll">
            {!session?.externalProcessingConsent && <p className="chat-hint">Confirm external processing consent in Transcript settings before sending a question.</p>}
            {!health?.gemini && <p className="chat-hint">Gemini is unavailable; questions and translations can be retried when configured.</p>}
            {answer ? (
              <div className="answer">
                <p>{answer.answer}</p>
                {answer.webAnswer && (
                  <p>
                    <strong>Public web:</strong> {answer.webAnswer}
                  </p>
                )}
                {answer.inference && (
                  <small>Model inference · verify against sources</small>
                )}
                <div>
                  {answer.evidence?.map((ev, i) => (
                    <button
                      className="citation"
                      key={i}
                      onClick={() => jump(ev)}
                    >
                      {ev.title || ev.label || ev.kind}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <p className="chat-hint">Ask about processed lecture content.</p>
            )}
          </div>
          {(selection || focusMaterialId) && <div className="selected-source"><span>{selection ? `Selected transcript · ${time(selection.startMs)}–${time(selection.endMs)} · “${selection.text}”` : `Selected material · ${session?.materials.find((item) => item.id === focusMaterialId)?.name}`}</span><button aria-label="Clear selected source" onClick={() => { setSelection(null); setFocusMaterialId(null); }}>×</button></div>}
          <label className="web-toggle">
            <input
              type="checkbox"
              checked={useWeb}
              onChange={(e) => setUseWeb(e.target.checked)}
            />{" "}
            Include public web search (sends this question to Gemini Search)
          </label>
          <form
            className="chat-form"
            onSubmit={(e) => {
              e.preventDefault();
              ask();
            }}
          >
            <label className="skip" htmlFor="chat-input">
              Your question
            </label>
            <input
              id="chat-input"
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              placeholder="Ask a question…"
            />
            <button disabled={!session || !!busy || !health?.gemini || !session.externalProcessingConsent} title={!session?.externalProcessingConsent ? "Confirm external processing consent in Transcript settings" : undefined}>
              Send
            </button>
          </form>
        </section>
      )}
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
