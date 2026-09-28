import { useLayoutEffect, useRef, useState } from "react";
import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { Markdown } from "../Component/Markdown";
import { api } from "./api";
import { recordedRange } from "./format";
import { TermExplanation } from "./TermExplanation";
import { ActivityPopover } from "./ActivityPopover";
import { useActivity } from "./useActivity";
import { Icon } from "../Component/Icon";
import { TermHighlight } from "./TermHighlight";

export function NotesPanel({ model }: { model: PlaybackController }) {
  const {
    session,
    noteMode,
    setNoteMode,
    markdown,
    setMarkdown,
    savedMarkdown,
    busy,
    jump,
    action,
    refresh,
  } = model;
  const editorValue = useRef(markdown);
  const body = useRef<HTMLDivElement>(null);
  const [topics, setTopics] = useState<{ title: string; level: number }[]>([]);
  useLayoutEffect(() => {
    const headings = body.current?.querySelectorAll<HTMLElement>(".note-content h1, .note-content h2, .note-content h3");
    setTopics(noteMode === "preview" && headings
      ? Array.from(headings, heading => ({ title: heading.textContent ?? "", level: Number(heading.tagName.slice(1)) }))
      : []);
  }, [markdown, noteMode, session?.id]);
  const scroll = useRef({ sessionId: session?.id, top: 0, follow: false, text: "", offset: 0 });
  const rememberScroll = () => {
    const element = body.current;
    if (!element) return;
    const top = element.getBoundingClientRect().top;
    const block = Array.from(element.querySelectorAll<HTMLElement>(".markdown-preview > *"))
      .find(item => item.getBoundingClientRect().bottom > top);
    scroll.current = { sessionId: session?.id, top: element.scrollTop,
      follow: element.scrollHeight > element.clientHeight && element.scrollHeight - element.scrollTop - element.clientHeight < 24,
      text: block?.textContent ?? "", offset: block ? block.getBoundingClientRect().top - top : 0 };
  };
  const activity = useActivity(session?.id, model.health?.aiActivity);
  const runningNote = activity.items.find(item => item.task === "Note revision" && item.status === "running"
    && item.basedOnVersion === session?.noteVersion);
  useLayoutEffect(() => {
    const element = body.current;
    if (!element || noteMode !== "preview") return;
    const previous = scroll.current;
    if (previous.sessionId !== session?.id) element.scrollTop = 0;
    else if (previous.follow) element.scrollTop = element.scrollHeight;
    else {
      const anchor = Array.from(element.querySelectorAll<HTMLElement>(".markdown-preview > *"))
        .find(item => item.textContent === previous.text);
      element.scrollTop = anchor
        ? element.scrollTop + anchor.getBoundingClientRect().top - element.getBoundingClientRect().top - previous.offset
        : previous.top;
    }
    rememberScroll();
  }, [markdown, runningNote?.draft, session?.id, noteMode, topics]);
  const queued = session?.transcripts.some(entry => entry.noteStatus === "pending" || entry.noteStatus === "processing");
  const [referenceId, setReferenceId] = useState<string | null>(null);
  const reference = session?.termInsights?.find(
    (insight) => insight.id === referenceId,
  );
  const transcriptChars = session?.transcripts.reduce(
    (sum, entry) => sum + entry.original.trim().length, 0,
  ) ?? 0;
  const waitingForSpeech = transcriptChars > 0 && transcriptChars < 40 && !session?.noteMarkdown;
  const failedNotes = session?.transcripts.filter((entry) => entry.noteStatus === "failed").length ?? 0;
  const keyTerms = session?.termInsights?.filter(insight => insight.highlight &&
    (!insight.outputLanguage || insight.outputLanguage === session.noteLanguage)) ?? [];
  const latestNote = activity.items.find(item => item.task === "Note revision" && item.basedOnVersion === session?.noteVersion);
  const draftNote = runningNote ?? latestNote;
  const noteStatus = runningNote ? (runningNote.draft ? "Editing…" : "Analyzing…")
    : busy === "generate" || queued ? "Queued…" : null;
  editorValue.current = markdown;
  return (
    <Panel className="notes-panel">
      <div className="panel-head">
        <div className="note-title">
          <h2>Lecture notes</h2>
          <span>v{session?.noteVersion || 0}</span>
          <ActivityPopover key={session?.id} session={session} items={activity.items} error={activity.error} onSource={jump} />
          {noteStatus && <span className="note-ai-status" role="status"><Icon name="pen" />{noteStatus}</span>}
        </div>
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
            <button aria-pressed={noteMode === "draft"} onClick={() => setNoteMode("draft")}>
              Live draft{runningNote && <span className="draft-dot" aria-hidden="true" />}
            </button>
          </div>
        </div>
      </div>
      <div className="notes-body" ref={body} onScroll={rememberScroll}>
        {noteMode === "preview" ? (
          markdown ? (
            <>
              {topics.length > 1 && <details className="note-outline" open>
                <summary>Topic tree · {topics.length}</summary>
                <nav aria-label="Note topics">
                  <ol>
                    {topics.map((topic, index) => <li key={`${index}-${topic.title}`}
                      style={{ marginLeft: (topic.level - Math.min(...topics.map(item => item.level))) * 14 }}>
                      <button type="button" onClick={() => {
                        const element = body.current;
                        const heading = element?.querySelectorAll<HTMLElement>(".note-content h1, .note-content h2, .note-content h3")[index];
                        if (element && heading) {
                          element.scrollTop += heading.getBoundingClientRect().top - element.getBoundingClientRect().top - 12;
                          heading.tabIndex = -1;
                          heading.focus({ preventScroll: true });
                        }
                      }}>{topic.title}</button>
                    </li>)}
                  </ol>
                </nav>
              </details>}
              <div className="note-content">
              <Markdown
                value={markdown}
                onReference={setReferenceId}
                groups={session?.sourceGroups}
                onPlaySources={ids => model.togglePlayback("note-passage-" + ids.join("-"), (session?.transcripts ?? []).filter(entry => ids.includes(entry.id)))}
                terms={keyTerms.map(insight => ({ id: insight.id, term: insight.term }))}
                renderTerm={(id, text) => {
                  const insight = keyTerms.find(item => item.id === id);
                  return insight && session ? <TermHighlight sessionId={session.id} insight={insight} text={text}
                    onAsk={() => model.askTerm({ text: insight.term, transcriptIds: insight.transcriptIds, materialIds: insight.materialIds })} /> : text;
                }}
                sources={[
                  ...(session?.transcripts || []).map((entry) => ({
                    id: entry.id,
                    label: recordedRange(entry.recordedAt, session?.createdAt, entry.startMs, entry.endMs).start,
                  })),
                  ...(session?.materials || []).map((entry) => ({ id: entry.id, label: entry.name })),
                ]}
                onSource={(id) => jump({ kind: session?.materials.some((entry) => entry.id === id) ? "material" : "lecture", id })}
              />
              </div>
              {!!session?.currentNote && (
                <>
                  <details className="note-sources">
                    <summary>
                      {session.currentNote.author === "user"
                        ? "Linked passages from earlier notes"
                        : "Linked transcript and materials"}{" "}
                      · {session.currentNote.transcriptIds.length} audio ·{" "}
                      {session.currentNote.materialIds.length} materials
                    </summary>
                    {session.currentNote.transcriptIds.map((id) => {
                      const source = session.transcripts.find(
                        (entry) => entry.id === id,
                      );
                      return (
                        source && (
                          <button
                            className="citation"
                            key={id}
                            onClick={() => jump({ kind: "lecture", id })}
                          >
                            {recordedRange(source.recordedAt, session.createdAt, source.startMs, source.endMs).start}
                          </button>
                        )
                      );
                    })}
                    {session.currentNote.materialIds.map((id) => {
                      const source = session.materials.find(
                        (entry) => entry.id === id,
                      );
                      return (
                        source && (
                          <button
                            className="citation"
                            key={id}
                            onClick={() => jump({ kind: "material", id })}
                          >
                            {source.name}
                          </button>
                        )
                      );
                    })}
                  </details>
                  <details className="note-changes">
                    <summary>
                      Changes in v{session.noteVersion} ·{" "}
                      {session.currentNote.edits?.length || 0}
                    </summary>
                    {session.currentNote.edits?.length ? (
                      <ol>
                        {session.currentNote.edits.map((edit, index) => (
                          <li key={`${edit.kind}-${edit.line}-${index}`}>
                            <small>
                              {edit.kind === "insert" ? "Added" : "Removed"} ·
                              line {edit.line}
                            </small>
                            <p>{edit.text || "(blank line)"}</p>
                            {edit.transcriptIds.map((id) => {
                              const source = session.transcripts.find(
                                (entry) => entry.id === id,
                              );
                              return (
                                source && (
                                  <button
                                    className="citation"
                                    key={id}
                                    onClick={() =>
                                      jump({ kind: "lecture", id })
                                    }
                                  >
                                    {recordedRange(source.recordedAt, session.createdAt, source.startMs, source.endMs).start}
                                  </button>
                                )
                              );
                            })}
                          </li>
                        ))}
                      </ol>
                    ) : (
                      <p className="empty">
                        No recorded line changes in this version.
                      </p>
                    )}
                  </details>
                </>
              )}
            </>
          ) : (
            <p className="empty">
              Notes will appear after enough speech is transcribed, or when you write them.
            </p>
          )
        ) : noteMode === "draft" ? (
          <section className="note-draft" aria-label="Live note draft">
            <p className="draft-label">Live draft · unverified citations · not saved</p>
            {draftNote?.draft ? <Markdown value={draftNote.draft} groups={session?.sourceGroups} sources={[
              ...(session?.transcripts ?? []).map(entry => ({ id: entry.id, label: recordedRange(entry.recordedAt, session?.createdAt, entry.startMs, entry.endMs).start })),
              ...(session?.materials ?? []).map(entry => ({ id: entry.id, label: entry.name })),
            ]} /> : <p className="empty">{runningNote ? "Analyzing confirmed sources. The draft will appear as it is written." : "No live revision is running. Saved notes are available in Preview."}</p>}
            {draftNote?.status === "failed" && <p role="alert">This revision failed. The draft was not saved.</p>}
          </section>
        ) : (
          <div className="editor-wrap">
            <label htmlFor="note-editor" className="sr-only">
              Editable Markdown
            </label>
            <textarea
              id="note-editor"
              className="markdown-editor"
              value={markdown}
              onChange={(e) => {
                editorValue.current = e.target.value;
                setMarkdown(e.target.value);
              }}
              placeholder="# Lecture notes&#10;&#10;```mermaid&#10;flowchart LR&#10;Audio --> Notes&#10;```"
            />
          </div>
        )}
      </div>
      {waitingForSpeech ? (
        <p className="note-status" role="status">Waiting for more recognized speech before generating AI notes.</p>
      ) : failedNotes > 0 && (
        <p className="note-status" role="status">
          Notes update failed for{" "}
          {failedNotes}{" "}
          transcript entries. Retry with “Revise with AI”.
        </p>
      )}
      <footer className="note-footer">
        <button
          className="save-button"
          disabled={!session || !!busy || markdown === savedMarkdown.current}
          onClick={() =>
            action("save", async () => {
              await api(`/sessions/${session!.id}/notes`, "POST", {
                markdown: editorValue.current,
              });
              await refresh(session!.id);
            })
          }
        >
          Save
        </button>
        <button
          className="secondary-action"
          disabled={!session || !!busy}
          onClick={() =>
            action("generate", async () => {
              const currentMarkdown = editorValue.current;
              if (currentMarkdown !== savedMarkdown.current) {
                await api(`/sessions/${session!.id}/notes`, "POST", {
                  markdown: currentMarkdown,
                });
                await refresh(session!.id);
              }
              await api(`/sessions/${session!.id}/notes/generate`, "POST");
              await refresh(session!.id);
            })
          }
        >
          Revise with AI
        </button>
      </footer>
      {reference && session && (
        <TermExplanation
          key={reference.id}
          sessionId={session.id}
          insight={reference}
          onAsk={() => { model.askTerm({ text: reference.term, transcriptIds: reference.transcriptIds, materialIds: reference.materialIds }); setReferenceId(null); }}
          onClose={() => setReferenceId(null)}
        />
      )}
    </Panel>
  );
}
