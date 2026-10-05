import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { Panel, PanelHeader } from "../../components/layout/Panel";
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
import { Icon } from "../../components/Icon";
import { LazyDetails } from "../../components/LazyDetails";
import { Segment, Segmented } from "../../components/Segmented";
import { Chip } from "../../components/Chip";
import { EmptyState } from "../../components/EmptyState";
import { Button } from "../../components/Button";

// A line under the notes saying why AI notes have not appeared or failed.
const statusStyle = "mx-4.5 mb-2.5 text-[11px] text-[#9a591c]";
// A tinted strip over the footer when saved notes and the editor's draft diverge.
const conflictStyle = "shrink-0 bg-accent-soft px-4 py-2 text-[12px]";

export function NotesPanel({
  className,
  session,
  player,
  onOpenSource,
}: {
  className?: string;
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
    <Panel className={className} data-testid="notes-panel">
      <PanelHeader
        wrap
        title={
          <div className="flex min-w-0 flex-wrap items-center gap-2.5">
            <h2 className="text-[15px] font-[750]">Lecture notes</h2>
            <span className="text-[11px] font-semibold text-muted">
              v{session?.noteVersion || 0}
            </span>
            <ActivityPopover
              key={session?.id}
              session={session}
              items={activity.items}
              error={activity.error}
              onSource={onOpenSource}
              onNotesRestored={() => refreshWorkspace(session!.id)}
            />
            {noteStatus && (
              <span
                className="inline-flex items-center gap-1.25 rounded-full bg-accent-soft px-2 py-1.25 text-[11px] font-[750] whitespace-nowrap text-accent"
                role="status"
                data-testid="note-ai-status"
              >
                <Icon
                  name="pen"
                  className="size-3.5 animate-note-pen motion-reduce:animate-none"
                />
                {noteStatus}
              </span>
            )}
          </div>
        }
        actions={
          <Segmented>
            <Segment
              pressed={noteMode === "preview"}
              onClick={() => setNoteMode("preview")}
            >
              Preview
            </Segment>
            <Segment
              pressed={noteMode === "markdown"}
              onClick={() => setNoteMode("markdown")}
            >
              Edit
            </Segment>
            <Segment pressed={reading} onClick={() => setReading(true)}>
              Reading
            </Segment>
            <Segment pressed={!reading} onClick={() => setReading(false)}>
              Sources
            </Segment>
            <Segment
              pressed={noteMode === "draft"}
              onClick={() => setNoteMode("draft")}
            >
              Live draft
              {runningNote && (
                <span
                  className="ml-1.25 inline-block size-1.25 rounded-full bg-accent"
                  aria-hidden="true"
                />
              )}
            </Segment>
          </Segmented>
        }
      />
      {/* Rendered notes keep a readable line length. */}
      <div
        className="min-h-0 flex-1 overflow-y-auto overscroll-contain p-5.5 **:data-markdown:max-w-[72ch]"
        data-testid="notes-body"
        ref={body}
        onScroll={rememberScroll}
      >
        {noteMode === "preview" ? (
          markdown ? (
            <>
              {topics.length > 1 && (
                <details
                  className="mb-5.5 rounded-[10px] border border-line bg-accent-soft p-3.5"
                  open
                >
                  <summary className="cursor-pointer text-[12px] font-bold">
                    Topic tree · {topics.length}
                  </summary>
                  <nav aria-label="Note topics">
                    <ol className="mt-3">
                      {topics.map((topic, index) => (
                        <li
                          className="border-l-2 border-line py-1 pl-3"
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
                            className="cursor-pointer text-left text-[12px] text-accent"
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
                    <p className="mb-4">
                      Saved source links are evidence metadata. Section points
                      show what was written; deferred inputs remain pending.
                    </p>
                  )}
                  <LazyDetails
                    className="mt-3 border-t border-line pt-2.5"
                    summaryClassName="cursor-pointer text-[12px] font-bold text-muted"
                    summary={
                      <>
                        Changes in v{session.noteVersion} ·{" "}
                        {session.currentNote.edits?.length || 0}
                      </>
                    }
                  >
                    {() =>
                      session.currentNote!.edits?.length ? (
                        <ol className="my-2.5 grid list-decimal gap-2.5 pl-5">
                          {session.currentNote!.edits.map((edit, index) => (
                            <li
                              className="pl-0.5"
                              key={`${edit.kind}-${edit.line}-${index}`}
                            >
                              <small className="text-[11px] text-muted">
                                {edit.kind === "insert" ? "Added" : "Removed"} ·
                                line {edit.line}
                              </small>
                              <p className="my-0.75 text-[12px] whitespace-pre-wrap wrap-anywhere">
                                {edit.text || "(blank line)"}
                              </p>
                              {edit.transcriptIds.map((id) => {
                                const source = session.transcripts.find(
                                  (entry) => entry.id === id,
                                );
                                return (
                                  source && (
                                    <Chip
                                      key={id}
                                      onClick={() =>
                                        onOpenSource({ kind: "lecture", id })
                                      }
                                    >
                                      {String(source.startMs / 1000) + "s"}
                                    </Chip>
                                  )
                                );
                              })}
                            </li>
                          ))}
                        </ol>
                      ) : (
                        <EmptyState>
                          No recorded line changes in this version.
                        </EmptyState>
                      )
                    }
                  </LazyDetails>
                </>
              )}
            </>
          ) : (
            <EmptyState>
              Notes will appear after enough speech is transcribed, or when you
              write them.
            </EmptyState>
          )
        ) : noteMode === "draft" ? (
          <section
            className="rounded-lg border border-dashed border-accent p-3.5"
            aria-label="Live note draft"
          >
            <p className="mb-4.5 text-[11px] font-bold text-accent">
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
              <EmptyState>
                {runningNote
                  ? "Analyzing confirmed sources. The draft will appear as it is written."
                  : "No live revision is running. Saved notes are available in Preview."}
              </EmptyState>
            )}
            {draftNote?.status === "failed" && (
              <p role="alert">This revision failed. The draft was not saved.</p>
            )}
          </section>
        ) : (
          <div className="flex h-full min-h-0 flex-1">
            <label htmlFor="note-editor" className="sr-only">
              Editable Markdown
            </label>
            <textarea
              id="note-editor"
              className="block min-h-full w-full flex-1 resize-none rounded-[10px] border border-line bg-white p-3.5 font-[Consolas,'Cascadia_Code',monospace] text-[12px] leading-[1.7] text-ink placeholder:text-[#757575]"
              value={markdown}
              onChange={(e) => draft.setText(e.target.value)}
              placeholder="# Lecture notes&#10;&#10;```mermaid&#10;flowchart LR&#10;Audio --> Notes&#10;```"
            />
          </div>
        )}
      </div>
      {waitingForSpeech ? (
        <p className={statusStyle} role="status">
          Waiting for more recognized speech before generating AI notes.
        </p>
      ) : (
        failedNotes > 0 && (
          <p className={statusStyle} role="status">
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
      <div className="flex flex-wrap items-center gap-2 border-t border-line px-4 py-2">
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
        <div className={conflictStyle} role="alert">
          <p className="my-1">
            Saved notes changed while you were editing. Your draft and caret are
            retained; review the current notes before saving.
          </p>
          <details>
            <summary>
              Read current saved notes · v{session!.noteVersion}
            </summary>
            <pre className="max-h-40 overflow-auto whitespace-pre-wrap wrap-anywhere">
              {session!.noteMarkdown}
            </pre>
          </details>
          <Button className="mr-2" onClick={draft.takeSavedVersion}>
            Load current notes and keep my draft for recovery
          </Button>
        </div>
      )}
      {draft.setAside && (
        <div className={conflictStyle}>
          <Button className="mr-2" onClick={draft.recoverSetAside}>
            Recover my previous draft
          </Button>
          <Button
            className="mr-2"
            onClick={() =>
              runAction("copy", () =>
                navigator.clipboard.writeText(draft.setAside!.text),
              )
            }
          >
            Copy previous draft
          </Button>
        </div>
      )}
      <footer className="flex flex-none gap-2 border-t border-line px-3.5 py-2.5 md:px-5 md:py-3">
        <Button
          variant="primary"
          className="disabled:cursor-not-allowed disabled:opacity-45"
          disabled={!session || !!busy || draft.conflict || !draft.isDirty}
          onClick={() => runAction("save", draft.save)}
        >
          Save
        </Button>
        <Button
          className="disabled:cursor-not-allowed disabled:opacity-45"
          disabled={!session || !!busy || draft.conflict}
          onClick={() => runAction("generate", draft.reviseWithAi)}
        >
          Revise with AI
        </Button>
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
