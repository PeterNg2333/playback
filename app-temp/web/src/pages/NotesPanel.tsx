import { useRef, useState } from "react";
import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { Markdown } from "../Component/Markdown";
import { api } from "./api";
import { time } from "./format";
import { TermExplanation } from "./TermExplanation";

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
  const [referenceId, setReferenceId] = useState<string | null>(null);
  const reference = session?.termInsights?.find(
    (insight) => insight.id === referenceId,
  );
  editorValue.current = markdown;
  return (
    <Panel className="notes-panel">
      <div className="panel-head">
        <div className="note-title">
          <h2>Lecture notes</h2>
          <span>v{session?.noteVersion || 0}</span>
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
          </div>
        </div>
      </div>
      <div className="notes-body">
        {noteMode === "preview" ? (
          markdown ? (
            <>
              <Markdown value={markdown} onReference={setReferenceId} />
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
                            {time(source.startMs)}–{time(source.endMs)}
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
                                    {time(source.startMs)}
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
              Notes will appear after you edit or generate them.
            </p>
          )
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
      {!!session?.transcripts.filter((entry) => entry.noteStatus === "failed")
        .length && (
        <p className="note-status" role="status">
          Notes update failed for{" "}
          {
            session.transcripts.filter((entry) => entry.noteStatus === "failed")
              .length
          }{" "}
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
          onClose={() => setReferenceId(null)}
        />
      )}
    </Panel>
  );
}
