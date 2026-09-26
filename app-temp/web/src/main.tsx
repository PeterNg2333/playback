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
  original: string;
  translation?: string;
  revision?: string;
  uncertain: boolean;
};
type Material = { id: string; name: string; text: string };
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
  noteMarkdown: string;
  noteVersion: number;
  materials: Material[];
  transcripts: Transcript[];
  chunks: Chunk[];
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
  state: "idle" | "recording";
  sessionId?: string;
  bytes: Record<string, number>;
  autoAsr?: boolean;
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
    name === "wave" ? (
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

function App() {
  const [theme, setTheme] = useState("pulse"),
    [themeMenu, setThemeMenu] = useState(false);
  const [view, setView] = useState<"notes" | "transcript" | "sources">(
    "transcript",
  );
  const [noteMode, setNoteMode] = useState<"preview" | "markdown">("preview");
  const [bilingual, setBilingual] = useState(false),
    [chatOpen, setChatOpen] = useState(false);
  const [session, setSession] = useState<Session | null>(null),
    [sessions, setSessions] = useState<{ id: string; title: string }[]>([]);
  const [health, setHealth] = useState<Health | null>(null),
    [capture, setCapture] = useState<CaptureStatus | null>(null),
    [markdown, setMarkdown] = useState(""),
    [question, setQuestion] = useState(""),
    [answer, setAnswer] = useState<Answer | null>(null);
  const [explanation, setExplanation] = useState<Answer | null>(null),
    [term, setTerm] = useState(""),
    [busy, setBusy] = useState(""),
    [error, setError] = useState("");
  const [useWeb, setUseWeb] = useState(false);
  const audio = useRef<HTMLAudioElement>(null);
  const savedMarkdown = useRef("");
  useEffect(() => {
    document.body.dataset.theme = theme;
    document.body.dataset.view = view;
    document.body.dataset.language = bilingual ? "bilingual" : "original";
  }, [theme, view, bilingual]);
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
    const list = await api<{ id: string; title: string }[]>("/sessions");
    setSessions(list);
    const chosen =
      id || localStorage.getItem("playback-session") || list[0]?.id;
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
  async function create() {
    const title = prompt("Session title");
    if (!title?.trim()) return;
    await action("create", async () => {
      const item = await api<{ id: string }>("/sessions", "POST", {
        title: title.trim(),
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
        }),
      );
      setQuestion("");
    });
  }
  async function explain(value: string) {
    setTerm(value);
    setExplanation(null);
    await action("explain", async () =>
      setExplanation(await api<Answer>("/explain", "POST", { term: value })),
    );
  }
  async function record(command: "start" | "stop") {
    if (command === "start" && !session) return;
    await action("capture", async () => {
      const status = await api<CaptureStatus>(
        `/capture/${command}`,
        "POST",
        command === "start" ? { sessionId: session!.id } : undefined,
      );
      setCapture(status);
      await refresh(session?.id);
    });
  }
  async function retryPendingAsr() {
    if (!session || !health?.automaticAsr) return;
    await action("retry-all", async () => {
      await api(`/sessions/${session.id}/asr/queue`, "POST");
      await refresh(session.id);
    });
  }
  return (
    <div
      data-theme={theme}
      data-view={view}
      data-language={bilingual ? "bilingual" : "original"}
    >
      <a className="skip" href="#workspace">
        Skip to workspace
      </a>
      <header className="topbar">
        <div className="brand">
          <span className="brand-icon">
            <Icon name={theme} />
          </span>
          <span className="brand-title">{session?.title || "Playback"}</span>
        </div>
        <div className="top-actions">
          <select
            aria-label="Select session"
            value={session?.id || ""}
            onChange={(e) => action("load", () => refresh(e.target.value))}
          >
            <option value="">Select session</option>
            {sessions.map((s) => (
              <option key={s.id} value={s.id}>
                {s.title}
              </option>
            ))}
          </select>
          <button type="button" onClick={create}>
            New session
          </button>
          <div className="theme-wrap">
            <button
              className="theme-toggle"
              aria-expanded={themeMenu}
              onClick={() => setThemeMenu(!themeMenu)}
            >
              <Icon name={theme} />
              <span>Theme</span>
            </button>
            {themeMenu && (
              <div className="theme-menu">
                <p className="theme-menu-title">Playback themes & icons</p>
                {["pulse", "wave", "loop"].map((name) => (
                  <button
                    className="theme-option"
                    key={name}
                    aria-pressed={theme === name}
                    onClick={() => {
                      setTheme(name);
                      setThemeMenu(false);
                    }}
                  >
                    <span className="theme-icon">
                      <Icon name={name} />
                    </span>
                    <strong>{name[0].toUpperCase() + name.slice(1)}</strong>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      </header>
      <main className="workspace" id="workspace">
        <nav className="mobile-nav" aria-label="Workspace views">
          {(["notes", "transcript", "sources"] as const).map((v) => (
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
            <h2>Notes</h2>
            <div className="panel-actions">
              <span className="status">
                Revision {session?.noteVersion || 0}
              </span>
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
                  Markdown
                </button>
              </div>
            </div>
          </div>
          <div className="notes-body">
            <div className="document-meta">
              Lecture notes · {session?.transcripts.length || 0} processed
              ranges
            </div>
            {noteMode === "preview" ? (
              markdown ? (
                <Markdown value={markdown} />
              ) : (
                <p className="empty">
                  Notes will appear after you edit or generate them.
                </p>
              )
            ) : (
              <>
                <label htmlFor="note-editor">Editable Markdown</label>
                <textarea
                  id="note-editor"
                  className="markdown-editor"
                  value={markdown}
                  onChange={(e) => setMarkdown(e.target.value)}
                  placeholder="# Lecture notes&#10;&#10;```mermaid&#10;flowchart LR&#10;Audio --> Notes&#10;```"
                />
                <button
                  disabled={!session || !!busy}
                  onClick={() =>
                    action("save", async () => {
                      await api(`/sessions/${session!.id}/notes`, "POST", {
                        markdown,
                      });
                      await refresh(session!.id);
                    })
                  }
                >
                  Save revision
                </button>
              </>
            )}
            <button
              className="secondary-action"
              disabled={!session || !!busy || !health?.gemini}
              onClick={() =>
                action("generate", async () => {
                  await api(`/sessions/${session!.id}/notes/generate`, "POST");
                  await refresh(session!.id);
                })
              }
            >
              Revise with AI
            </button>
          </div>
        </section>
        <section className="panel transcript-panel">
          <div className="panel-head">
            <h2>Transcript</h2>
            <div className="panel-actions">
              <div className="transcript-head-tabs">
                <button
                  className="tab-button"
                  aria-current={view === "transcript" ? "page" : undefined}
                  onClick={() => setView("transcript")}
                >
                  Transcript
                </button>
                <button
                  className="tab-button"
                  aria-current={view === "sources" ? "page" : undefined}
                  onClick={() => setView("sources")}
                >
                  Sources {session?.materials.length || 0}
                </button>
              </div>
              <div className="segmented">
                <button
                  aria-pressed={!bilingual}
                  onClick={() => setBilingual(false)}
                >
                  Original
                </button>
                <button
                  aria-pressed={bilingual}
                  onClick={() => setBilingual(true)}
                >
                  Bilingual
                </button>
              </div>
            </div>
          </div>
          {view === "sources" ? (
            <div className="source-content">
              <button onClick={attach} disabled={!session || !!busy}>
                Attach text material
              </button>
              {session?.chunks.some((chunk) => chunk.status !== "transcribed") && (
                <>
                  <p className="asr-help">
                    {!health?.automaticAsr
                      ? "Restart the older server with pnpm.cmd dev to enable automatic ASR."
                      : "New recordings transcribe automatically. Queue earlier audio below."}
                  </p>
                  <button
                    disabled={!health?.automaticAsr || !!busy}
                    onClick={retryPendingAsr}
                  >
                    {busy === "retry-all" ? "Queueing…" : "Transcribe all pending audio"}
                  </button>
                </>
              )}
              {session?.materials.map((m) => (
                <article className="source-item" id={m.id} key={m.id}>
                  <span className="source-icon">↗</span>
                  <div>
                    <strong>{m.name}</strong>
                    <p>{m.text.slice(0, 180)}</p>
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
                    <p>{c.status}</p>
                    {c.error && <p className="capture-error" role="alert">{c.error}</p>}
                    {c.status !== "transcribed" && (
                      <button
                        disabled={!health?.automaticAsr || c.status === "transcribing" || !!busy}
                        onClick={() =>
                          action("retry", async () => {
                            await api(`/chunks/${c.id}/retry`, "POST");
                            await refresh(session!.id);
                          })
                        }
                      >
                        Retry ASR
                      </button>
                    )}
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
              <div className="content-meta">
                <strong>Lecture audio</strong>
                <span>
                  {session?.chunks.filter((c) => c.status !== "transcribed")
                    .length || 0}{" "}
                  pending
                </span>
              </div>
              <div className="capture-controls">
                <span>
                  {capture?.state === "recording"
                    ? `Recording locally · ${Object.entries(capture.bytes).map(([source, bytes]) => `${source} ${Math.round(bytes / 1024)} KB`).join(" · ")} · ${capture.autoAsr ? "automatic ASR on" : "audio saved locally"}`
                    : "Record on this Windows computer · microphone first, system audio if available · automatic ASR on"}
                </span>
                {capture?.state === "recording" ? (
                  <button disabled={!!busy} onClick={() => record("stop")}>Stop Recording</button>
                ) : (
                  <button disabled={!session || !!busy || !health?.mongo} onClick={() => record("start")}>
                    Start Recording
                  </button>
                )}
              </div>
              {capture?.error && <p className="capture-error" role="alert">{capture.error}</p>}
              <div className="rows">
                {session?.transcripts.length ? (
                  session.transcripts.map((t) => (
                    <article className="transcript-row" id={t.id} key={t.id}>
                      <button
                        className="time-range"
                        onClick={() => seek(t)}
                        aria-label={`Play audio from ${time(t.startMs)} to ${time(t.endMs)}`}
                      >
                        {time(t.startMs)}–{time(t.endMs)}
                      </button>
                      <div>
                        <span className="speaker">{t.sourceId}</span>
                        <p className="original">{t.original}</p>
                        {t.uncertain && (
                          <span className="uncertain">
                            Unclear · review audio
                          </span>
                        )}
                        {bilingual && t.translation && (
                          <p className="translation">
                            Translation: {t.translation}
                          </p>
                        )}
                        {t.revision && (
                          <p className="translation">
                            Suggested revision: {t.revision}
                          </p>
                        )}
                        {!t.translation && (
                          <button
                            className="term"
                            disabled={!health?.gemini || !!busy}
                            onClick={() =>
                              action("translate", async () => {
                                await api(
                                  `/sessions/${session!.id}/transcripts/${t.id}/translate`,
                                  "POST",
                                );
                                await refresh(session!.id);
                                setBilingual(true);
                              })
                            }
                          >
                            Translate
                          </button>
                        )}
                        <button
                          className="term"
                          onClick={() => { const selected = window.getSelection()?.toString().trim(); const candidate = selected || prompt("Term to explain"); if (candidate) explain(candidate) }}
                        >
                          Explain selected term
                        </button>
                      </div>
                    </article>
                  ))
                ) : (
                  <p className="empty">
                    No transcript yet. Saved audio appears under Sources and transcribes automatically.
                  </p>
                )}
              </div>
            </div>
          )}
        </section>
      </main>
      <audio ref={audio} hidden />
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
          <label className="web-toggle">
            <input
              type="checkbox"
              checked={useWeb}
              onChange={(e) => setUseWeb(e.target.checked)}
            />{" "}
            Include public web search
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
            <button disabled={!session || !!busy || !health?.gemini}>
              Send
            </button>
          </form>
        </section>
      )}
      {term && (
        <div className="dialog-backdrop">
          <section
            className="explanation-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="explanation-title"
          >
            <div className="dialog-head">
              <h2 id="explanation-title">{term}</h2>
              <button
                className="icon-button"
                onClick={() => setTerm("")}
                aria-label="Close explanation"
              >
                ×
              </button>
            </div>
            <div className="dialog-body">
              {explanation ? (
                <>
                  <p>{explanation.answer}</p>
                  <small>Model inference · review evidence</small>
                  <div>
                    {explanation.evidence?.map((ev, i) => (
                      <button
                        className="citation"
                        onClick={() => jump(ev)}
                        key={i}
                      >
                        {ev.title || ev.url}
                      </button>
                    ))}
                  </div>
                </>
              ) : (
                <p>
                  {busy === "explain"
                    ? "Finding evidence…"
                    : error || "No explanation available."}
                </p>
              )}
            </div>
          </section>
        </div>
      )}
    </div>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
