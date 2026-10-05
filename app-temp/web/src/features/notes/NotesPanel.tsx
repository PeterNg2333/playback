import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { Panel } from "../../components/layout/Panel";
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
import { Menu, MenuItem } from "../../components/Menu";
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
  const [noteDialog, setNoteDialog] = useState<"history" | "organize" | null>(
    null,
  );
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
      <header
        className="flex shrink-0 items-center gap-1 border-b border-line px-2 py-3 sm:gap-2 sm:px-2.5"
        data-testid="notes-toolbar"
      >
        <div className="flex h-10 min-w-0 flex-1 items-center gap-1 rounded-full border border-line bg-white px-2 sm:gap-2 sm:px-3">
          <h2
            className="min-w-0 flex-1 truncate text-[13px] font-bold max-sm:sr-only"
            title="Lecture notes"
          >
            Lecture notes
          </h2>
          <span className="hidden text-[10px] text-muted 2xl:inline">
            v{session?.noteVersion || 0}
          </span>
          <ActivityPopover
            compact
            key={session?.id}
            session={session}
            items={activity.items}
            error={activity.error}
            onSource={onOpenSource}
            onNotesRestored={() => refreshWorkspace(session!.id)}
          />
          {noteStatus && (
            <span
              className="shrink-0 text-accent max-sm:hidden"
              role="status"
              data-testid="note-ai-status"
              title={noteStatus}
            >
              <Icon
                name="pen"
                className="size-3.5 animate-note-pen motion-reduce:animate-none"
              />
              <span className="sr-only">{noteStatus}</span>
            </span>
          )}
          <label
            className="flex shrink-0 cursor-pointer items-center gap-1 text-[10px] text-muted has-disabled:opacity-50"
            title="Show source links"
          >
            <input
              type="checkbox"
              role="switch"
              aria-label="Show sources"
              checked={!reading}
              disabled={noteMode !== "preview"}
              onChange={(event) => setReading(!event.target.checked)}
              className="relative h-4 w-6 shrink-0 cursor-pointer appearance-none rounded-full bg-slate-300 before:absolute before:top-0.5 before:left-0.5 before:size-3 before:rounded-full before:bg-white checked:bg-accent checked:before:translate-x-2 disabled:cursor-default"
            />
            <span className="hidden 2xl:inline">Sources</span>
          </label>
          <select
            aria-label="Note view"
            className="w-20 shrink-0 rounded-full sm:w-24 bg-transparent py-1 text-[11px] font-semibold text-ink"
            value={noteMode}
            onChange={(event) =>
              setNoteMode(
                event.target.value === "markdown"
                  ? "markdown"
                  : event.target.value === "draft"
                    ? "draft"
                    : "preview",
              )
            }
          >
            <option value="preview">Notes</option>
            <option value="markdown">Edit draft</option>
            <option value="draft">Live AI edit</option>
          </select>
        </div>
        <div
          className="inline-flex h-9 shrink-0 items-stretch rounded-full border border-accent bg-white"
          role="group"
          aria-label="Save and note actions"
        >
          <button
            className="rounded-l-full bg-accent px-2.5 py-2 text-[12px] font-bold text-white disabled:cursor-not-allowed disabled:bg-accent-soft disabled:text-muted"
            disabled={!session || !!busy || draft.conflict || !draft.isDirty}
            onClick={() => runAction("save", draft.save)}
          >
            Save
          </button>
          <Menu
            key={session?.id + ":actions"}
            className="relative border-l border-accent"
            label="Note actions"
            dismissible
            summaryClassName="grid h-full w-6 place-items-center rounded-r-full text-accent hover:bg-accent-soft"
            summary={<Icon name="chevron-down" className="size-3.5" />}
          >
            {(close) => (
              <div className="absolute right-0 top-full z-30 mt-2 w-52 rounded-lg border border-line bg-white p-1 shadow-lg">
                <MenuItem
                  disabled={!session || !!busy || draft.conflict}
                  onClick={() => {
                    close();
                    void runAction("generate", draft.reviseWithAi);
                  }}
                >
                  Revise with AI
                </MenuItem>
                <div className="my-1 border-t border-line" />
                <MenuItem
                  disabled={!session || !health?.sectionNotes}
                  onClick={() => {
                    close();
                    setNoteDialog("history");
                  }}
                >
                  History & recovery
                </MenuItem>
                <MenuItem
                  disabled={!session?.currentNote?.sections?.length}
                  onClick={() => {
                    close();
                    setNoteDialog("organize");
                  }}
                >
                  Organize section…
                </MenuItem>
              </div>
            )}
          </Menu>
        </div>

        <NoteCoveragePanel
          key={session?.id + ":coverage"}
          session={session}
          draftDirty={draft.isDirty}
          onOpenSource={onOpenSource}
          latestRevision={latestNote}
        />
      </header>
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

      {noteDialog === "history" && (
        <NoteHistoryDialog
          key={session?.id}
          session={session}
          draftDirty={draft.isDirty}
          onClose={() => setNoteDialog(null)}
        />
      )}
      {noteDialog === "organize" && session && (
        <OrganizeSection
          key={session.id}
          session={session}
          draftDirty={draft.isDirty}
          onClose={() => setNoteDialog(null)}
        />
      )}
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
