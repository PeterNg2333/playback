import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { Panel } from "../../Component/Layout/Panel";
import { CitedMarkdown } from "../sources/CitedMarkdown";
import { useHealth } from "../../lib/useHealth";
import { askAboutTerm } from "../ask/askAbout";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { useNoteDraft } from "./useNoteDraft";
import type { Evidence, Session } from "../../lib/backend/schemas";
import { runAction, usePlaybackField, usePlaybackStore } from "../../lib/store";
import type { AudioPlayer } from "../player/useAudioPlayer";
import { NotePreview } from "./NotePreview";
import { NoteHistoryDialog } from "./NoteHistoryDialog";
import { OrganizeSection } from "./OrganizeSection";
import { NoteCoveragePanel } from "./NoteCoveragePanel";
import { TermExplanation } from "../terms/TermExplanation";
import { ActivityPopover } from "../activity/ActivityPopover";
import { useActivity } from "../activity/useActivity";
import { Icon } from "../../Component/Icon";
import { LazyDetails } from "../../Component/LazyDetails";

export function NotesPanel({
  session,
  player,
  onOpenSource,
}: {
  session: Session | null;
  player: AudioPlayer;
  onOpenSource: (source: Evidence) => void;
}) {
  const [noteMode, setNoteMode] = usePlaybackField("noteMode");
  const busy = usePlaybackStore((state) => state.busy);
  const health = useHealth();
  const draft = useNoteDraft(session);
  const markdown = draft.text;
  const [reading, setReading] = useState(true);
  const body = useRef<HTMLDivElement>(null);
  const renderedNotes = useRef<HTMLDivElement>(null);
  const headings = () =>
    renderedNotes.current?.querySelectorAll<HTMLElement>("h1, h2, h3") ?? [];
  const markdownBlocks = (element: HTMLElement) =>
    element.querySelectorAll<HTMLElement>("[data-markdown] > *");
  const [topics, setTopics] = useState<{ title: string; level: number }[]>([]);
  useLayoutEffect(() => {
    setTopics(
      noteMode === "preview"
        ? Array.from(headings(), (heading) => ({
            title: heading.textContent ?? "",
            level: Number(heading.tagName.slice(1)),
          }))
        : [],
    );
  }, [markdown, noteMode, session?.id]);
  const scroll = useRef({
    sessionId: session?.id,
    top: 0,
    follow: false,
    text: "",
    offset: 0,
  });
  const rememberScroll = () => {
    const element = body.current;
    if (!element) return;
    const top = element.getBoundingClientRect().top;
    const block = Array.from(markdownBlocks(element)).find(
      (item) => item.getBoundingClientRect().bottom > top,
    );
    scroll.current = {
      sessionId: session?.id,
      top: element.scrollTop,
      follow:
        element.scrollHeight > element.clientHeight &&
        element.scrollHeight - element.scrollTop - element.clientHeight < 24,
      text: block?.textContent ?? "",
      offset: block ? block.getBoundingClientRect().top - top : 0,
    };
  };
  const activity = useActivity(session?.id, health?.aiActivity);
  const runningNote = activity.items.find(
    (item) =>
      item.task === "Note revision" &&
      item.status === "running" &&
      item.basedOnVersion === session?.noteVersion,
  );
  useLayoutEffect(() => {
    const element = body.current;
    if (!element || noteMode !== "preview") return;
    const previous = scroll.current;
    if (previous.sessionId !== session?.id) element.scrollTop = 0;
    else if (previous.follow) element.scrollTop = element.scrollHeight;
    else {
      const anchor = Array.from(markdownBlocks(element)).find(
        (item) => item.textContent === previous.text,
      );
      element.scrollTop = anchor
        ? element.scrollTop +
          anchor.getBoundingClientRect().top -
          element.getBoundingClientRect().top -
          previous.offset
        : previous.top;
    }
    rememberScroll();
  }, [markdown, runningNote?.draft, session?.id, noteMode, topics]);
  const queued = useMemo(
    () =>
      session?.transcripts.some((entry) =>
        ["pending", "processing", "deferred"].includes(entry.noteStatus ?? ""),
      ),
    [session?.transcripts],
  );
  const draftSources = useMemo(
    () => [
      ...(session?.transcripts ?? []).map((entry) => ({
        id: entry.id,
        label: String(entry.startMs / 1000) + "s",
      })),
      ...(session?.materials ?? []).map((entry) => ({
        id: entry.id,
        label: entry.name,
      })),
    ],
    [session?.transcripts, session?.materials],
  );
  const [referenceId, setReferenceId] = useState<string | null>(null);
  const reference = session?.termInsights?.find(
    (insight) => insight.id === referenceId,
  );
  const transcriptChars = useMemo(
    () =>
      session?.transcripts.reduce(
        (sum, entry) => sum + entry.original.trim().length,
        0,
      ) ?? 0,
    [session?.transcripts],
  );
  const waitingForSpeech = transcriptChars === 0 && !session?.noteMarkdown;
  const failedNotes = useMemo(
    () =>
      session?.transcripts.filter((entry) => entry.noteStatus === "failed")
        .length ?? 0,
    [session?.transcripts],
  );

  const latestNote = activity.items.find(
    (item) =>
      item.task === "Note revision" &&
      item.basedOnVersion === session?.noteVersion,
  );
  const latestGate = activity.items.find(
    (item) =>
      item.task === "Jev note gate" &&
      item.basedOnVersion === session?.noteVersion,
  );
  const preparation = activity.items.find(
    (item) =>
      item.task === "Note input preparation" &&
      item.basedOnVersion === session?.noteVersion,
  );
  const draftNote = runningNote ?? latestNote;
  const noteStatus = runningNote
    ? runningNote.draft
      ? "Editing…"
      : "Analyzing…"
    : busy === "generate"
      ? "Generating…"
      : latestGate?.status === "running"
        ? "Deciding…"
        : queued && latestNote?.status === "failed"
          ? "Notes failed"
          : queued && latestGate?.status === "failed"
            ? "Note gate failed"
            : queued && preparation?.status === "failed"
              ? "Input needs review"
              : queued && latestGate?.summary?.startsWith("wait:")
                ? "Waiting for continuation"
                : queued && latestNote?.summary?.startsWith("No section change")
                  ? "Waiting for continuation"
                  : queued
                    ? health?.autoNotes === false
                      ? "Sources awaiting revision"
                      : "Pending decision…"
                    : null;
  return (
    <Panel className="notes-panel">
      <div className="panel-head">
        <div className="note-title">
          <h2>Lecture notes</h2>
          <span>v{session?.noteVersion || 0}</span>
          <ActivityPopover
            key={session?.id}
            session={session}
            items={activity.items}
            error={activity.error}
            onSource={onOpenSource}
            onNotesRestored={() => refreshWorkspace(session!.id)}
          />
          {noteStatus && (
            <span className="note-ai-status" role="status">
              <Icon name="pen" />
              {noteStatus}
            </span>
          )}
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
            <button aria-pressed={reading} onClick={() => setReading(true)}>
              Reading
            </button>
            <button aria-pressed={!reading} onClick={() => setReading(false)}>
              Sources
            </button>
            <button
              aria-pressed={noteMode === "draft"}
              onClick={() => setNoteMode("draft")}
            >
              Live draft
              {runningNote && <span className="draft-dot" aria-hidden="true" />}
            </button>
          </div>
        </div>
      </div>
      <div className="notes-body" ref={body} onScroll={rememberScroll}>
        {noteMode === "preview" ? (
          markdown ? (
            <>
              {topics.length > 1 && (
                <details className="note-outline" open>
                  <summary>Topic tree · {topics.length}</summary>
                  <nav aria-label="Note topics">
                    <ol>
                      {topics.map((topic, index) => (
                        <li
                          key={`${index}-${topic.title}`}
                          style={{
                            marginLeft:
                              (topic.level -
                                Math.min(...topics.map((item) => item.level))) *
                              14,
                          }}
                        >
                          <button
                            type="button"
                            onClick={() => {
                              const element = body.current;
                              const heading = headings()[index];
                              if (element && heading) {
                                element.scrollTop +=
                                  heading.getBoundingClientRect().top -
                                  element.getBoundingClientRect().top -
                                  12;
                                heading.tabIndex = -1;
                                heading.focus({ preventScroll: true });
                              }
                            }}
                          >
                            {topic.title}
                          </button>
                        </li>
                      ))}
                    </ol>
                  </nav>
                </details>
              )}
              <NotePreview
                key={session?.id}
                ref={renderedNotes}
                session={session}
                markdown={markdown}
                reading={reading}
                onReference={setReferenceId}
                onOpenSource={onOpenSource}
                onPlay={player.togglePlayback}
              />
              {!!session?.currentNote && (
                <>
                  {!reading && (
                    <p className="coverage-summary">
                      Saved source links are evidence metadata. Section points
                      show what was written; deferred inputs remain pending.
                    </p>
                  )}
                  <LazyDetails
                    className="note-changes"
                    summary={
                      <>
                        Changes in v{session.noteVersion} ·{" "}
                        {session.currentNote.edits?.length || 0}
                      </>
                    }
                  >
                    {() =>
                      session.currentNote!.edits?.length ? (
                        <ol>
                          {session.currentNote!.edits.map((edit, index) => (
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
                                        onOpenSource({ kind: "lecture", id })
                                      }
                                    >
                                      {String(source.startMs / 1000) + "s"}
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
                      )
                    }
                  </LazyDetails>
                </>
              )}
            </>
          ) : (
            <p className="empty">
              Notes will appear after enough speech is transcribed, or when you
              write them.
            </p>
          )
        ) : noteMode === "draft" ? (
          <section className="note-draft" aria-label="Live note draft">
            <p className="draft-label">
              Live draft · unverified citations · not saved
            </p>
            {draftNote?.draft ? (
              <CitedMarkdown
                key={session?.id}
                value={draftNote.draft}
                groups={session?.sourceGroups}
                sources={draftSources}
              />
            ) : (
              <p className="empty">
                {runningNote
                  ? "Analyzing confirmed sources. The draft will appear as it is written."
                  : "No live revision is running. Saved notes are available in Preview."}
              </p>
            )}
            {draftNote?.status === "failed" && (
              <p role="alert">This revision failed. The draft was not saved.</p>
            )}
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
              onChange={(e) => draft.setText(e.target.value)}
              placeholder="# Lecture notes&#10;&#10;```mermaid&#10;flowchart LR&#10;Audio --> Notes&#10;```"
            />
          </div>
        )}
      </div>
      {waitingForSpeech ? (
        <p className="note-status" role="status">
          Waiting for more recognized speech before generating AI notes.
        </p>
      ) : (
        failedNotes > 0 && (
          <p className="note-status" role="status">
            Notes update failed for {failedNotes} transcript entries. Retry with
            “Revise with AI”.
          </p>
        )
      )}
      <NoteCoveragePanel
        key={session?.id + ":coverage"}
        session={session}
        draftDirty={draft.isDirty}
        onOpenSource={onOpenSource}
      />
      <div className="note-tools">
        <NoteHistoryDialog
          key={session?.id}
          session={session}
          draftDirty={draft.isDirty}
        />
        {session && (
          <OrganizeSection
            key={session.id}
            session={session}
            draftDirty={draft.isDirty}
          />
        )}
      </div>
      {draft.conflict && (
        <div className="note-conflict" role="alert">
          <p>
            Saved notes changed while you were editing. Your draft and caret are
            retained; review the current notes before saving.
          </p>
          <details>
            <summary>
              Read current saved notes · v{session!.noteVersion}
            </summary>
            <pre>{session!.noteMarkdown}</pre>
          </details>
          <button onClick={draft.takeSavedVersion}>
            Load current notes and keep my draft for recovery
          </button>
        </div>
      )}
      {draft.setAside && (
        <div className="note-conflict">
          <button onClick={draft.recoverSetAside}>
            Recover my previous draft
          </button>
          <button
            onClick={() =>
              runAction("copy", () =>
                navigator.clipboard.writeText(draft.setAside!.text),
              )
            }
          >
            Copy previous draft
          </button>
        </div>
      )}
      <footer className="note-footer">
        <button
          className="save-button"
          disabled={!session || !!busy || draft.conflict || !draft.isDirty}
          onClick={() => runAction("save", draft.save)}
        >
          Save
        </button>
        <button
          className="secondary-action"
          disabled={!session || !!busy || draft.conflict}
          onClick={() => runAction("generate", draft.reviseWithAi)}
        >
          Revise with AI
        </button>
      </footer>
      {reference && session && (
        <TermExplanation
          key={reference.id}
          sessionId={session.id}
          insight={reference}
          onAsk={() => {
            askAboutTerm(session, {
              text: reference.term,
              transcriptIds: reference.transcriptIds,
              materialIds: reference.materialIds,
            });
            setReferenceId(null);
          }}
          onClose={() => setReferenceId(null)}
        />
      )}
    </Panel>
  );
}
