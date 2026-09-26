import type { PlaybackController } from "./handlers";
import { Panel } from "../Component/Layout/Panel";
import { Markdown } from "../Component/Markdown";
import { api } from "./api";
import { time } from "./format";

export function NotesPanel({ model }: { model: PlaybackController }) {
  const {
    session,
    noteMode,
    setNoteMode,
    markdown,
    setMarkdown,
    savedMarkdown,
    busy,
    health,
    jump,
    action,
    refresh,
  } = model;
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
              <Markdown value={markdown} />
              {!!session?.currentNote && (
                <details className="note-sources">
                  <summary>
                    {session.currentNote.author === "user"
                      ? "Source lineage from earlier notes"
                      : "Sources used for this note version"}{" "}
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
              onChange={(e) => setMarkdown(e.target.value)}
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
          disabled={
            !session ||
            !!busy ||
            !health?.gemini ||
            !session.externalProcessingConsent ||
            markdown !== savedMarkdown.current
          }
          title={
            !session?.externalProcessingConsent
              ? "Confirm external processing consent in Transcript settings"
              : markdown !== savedMarkdown.current
                ? "Save your edits before revising with AI"
                : undefined
          }
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
    </Panel>
  );
}
