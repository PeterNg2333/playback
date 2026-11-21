import clsx from "clsx";
import { useEffect, useRef, useState } from "react";
import { z } from "zod";
import { Dialog, DialogButton } from "../../components/Dialog";
import { api } from "../../lib/backend/client";
import type { Session } from "../../lib/backend/schemas";
import {
  buildDocx,
  downloadDocx,
  readTemplate,
  type ExportBlock,
} from "./docxTemplate";

// Export to Word as a conversation: pick (or upload) a Word template, ask for a first draft, then tell the AI what to
// change. Every reply keeps its version, so any version can be previewed and downloaded, and the conversation is saved
// with the session: closing the window loses nothing. The .docx keeps the template's layout, headers, footers and styles.

const TemplateSchema = z.object({
  id: z.string(),
  name: z.string(),
  size: z.number(),
  createdAt: z.string(),
});
const TemplatesSchema = z.array(TemplateSchema);
const TemplateFileSchema = z.object({
  id: z.string(),
  name: z.string(),
  contentBase64: z.string(),
});
const BlockSchema = z.object({
  kind: z.enum(["heading", "paragraph", "bullet", "table"]),
  level: z.number(),
  like: z.number(),
  text: z.string(),
  rows: z.array(z.object({ cells: z.array(z.string()) })),
});
const TurnSchema = z.object({
  id: z.string(),
  role: z.enum(["user", "assistant"]),
  text: z.string(),
  templateId: z.string().nullish(),
  templateName: z.string().nullish(),
  provider: z.string().nullish(),
  model: z.string().nullish(),
  noteVersion: z.number().nullish(),
  createdAt: z.string(),
  blocks: z.array(BlockSchema).nullish(),
});
const TurnsSchema = z.array(TurnSchema);
type Turn = z.infer<typeof TurnSchema>;

const MAX_TEMPLATE_BYTES = 5_000_000;
const fieldStyle =
  "w-full rounded-lg border border-line bg-white p-2 text-[13px] font-normal text-ink";

function toBase64(bytes: Uint8Array) {
  let binary = "";
  for (let i = 0; i < bytes.length; i += 0x8000)
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return btoa(binary);
}
function fromBase64(text: string) {
  const binary = atob(text);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
const message = (error: unknown) =>
  error instanceof Error ? error.message : String(error);
const timeOf = (iso: string) =>
  new Date(iso).toLocaleString([], {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });

function pickDocx() {
  return new Promise<File | null>((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept =
      ".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    input.hidden = true;
    document.body.append(input);
    input.onchange = () => {
      const selected = input.files?.[0] || null;
      input.remove();
      resolve(selected);
    };
    input.oncancel = () => {
      input.remove();
      resolve(null);
    };
    input.click();
  });
}

function Preview({ blocks }: { blocks: ExportBlock[] }) {
  return (
    <div className="grid max-h-[40dvh] gap-2 overflow-y-auto rounded-lg border border-line bg-white p-4 text-[12px] leading-[1.5]">
      {blocks.map((block, index) => {
        if (block.kind === "table")
          return (
            <table key={index} className="w-full border-collapse text-[11px]">
              <tbody>
                {block.rows.map((row, r) => (
                  <tr
                    key={r}
                    className={r === 0 ? "bg-accent-soft font-bold" : ""}
                  >
                    {row.cells.map((cell, c) => (
                      <td
                        key={c}
                        className="border border-line px-1.5 py-1 align-top"
                      >
                        {cell}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          );
        if (block.kind === "heading")
          return (
            <p
              key={index}
              className={
                block.level <= 1
                  ? "mt-1 text-[15px] font-bold"
                  : "mt-1 text-[13px] font-bold"
              }
            >
              {block.text}
            </p>
          );
        return (
          <p key={index} className="whitespace-pre-wrap">
            {block.kind === "bullet" ? "• " : ""}
            {block.text}
          </p>
        );
      })}
    </div>
  );
}

export function ExportDialog({
  session,
  onClose,
}: {
  session: Session;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const [templates, setTemplates] = useState<z.infer<typeof TemplatesSchema>>(
    [],
  );
  const [templateId, setTemplateId] = useState("");
  const [turns, setTurns] = useState<Turn[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [pending, setPending] = useState<string | null>(null);
  const [busy, setBusy] = useState<
    "" | "loading" | "upload" | "send" | "download" | "delete" | "clear"
  >("loading");
  const [error, setError] = useState("");

  const versions = turns.filter((turn) => turn.blocks?.length);
  const latest = versions.at(-1);
  const versionNumber = (turn: Turn) => versions.indexOf(turn) + 1;

  async function loadTemplates(select?: string) {
    const list = await api.get("/export-templates", TemplatesSchema);
    setTemplates(list);
    setTemplateId(
      (current) =>
        select ??
        (list.some((x) => x.id === current) ? current : (list[0]?.id ?? "")),
    );
    return list;
  }
  useEffect(() => {
    dialog.current?.showModal();
    (async () => {
      const [list, saved] = await Promise.all([
        loadTemplates(),
        api.get(`/sessions/${session.id}/export/conversation`, TurnsSchema),
      ]);
      setTurns(saved);
      const last = saved.filter((turn) => turn.blocks?.length).at(-1);
      setOpen(last?.id ?? null);
      // Continue with the template the latest version was written for.
      if (last?.templateId && list.some((x) => x.id === last.templateId))
        setTemplateId(last.templateId);
    })()
      .catch((failure) => setError(message(failure)))
      .finally(() => setBusy(""));
  }, []);
  useEffect(() => {
    bottom.current?.scrollIntoView({ block: "end" });
  }, [turns.length, pending]);

  async function run(kind: typeof busy, work: () => Promise<void>) {
    setBusy(kind);
    setError("");
    try {
      await work();
    } catch (failure) {
      setError(message(failure));
    } finally {
      setBusy("");
    }
  }

  const upload = () =>
    run("upload", async () => {
      const file = await pickDocx();
      if (!file) return;
      if (!/\.docx$/i.test(file.name))
        throw new Error("Choose a Word document (.docx).");
      if (file.size > MAX_TEMPLATE_BYTES)
        throw new Error("The template must be 5 MB or smaller.");
      const bytes = new Uint8Array(await file.arrayBuffer());
      const { outline } = await readTemplate(bytes);
      const saved = await api.post(
        "/export-templates",
        { name: file.name, contentBase64: toBase64(bytes), outline },
        TemplateSchema,
      );
      await loadTemplates(saved.id);
    });

  const removeTemplate = () =>
    run("delete", async () => {
      const template = templates.find((x) => x.id === templateId);
      if (
        !template ||
        !window.confirm(
          `Delete the template "${template.name}"? Versions written with it can no longer be downloaded.`,
        )
      )
        return;
      await api.delete(`/export-templates/${template.id}`);
      await loadTemplates("");
    });

  const send = (text: string) =>
    run("send", async () => {
      setPending(text);
      try {
        const added = await api.post(
          `/sessions/${session.id}/export/message`,
          { templateId, message: text || null },
          TurnsSchema,
        );
        setTurns((current) => [...current, ...added]);
        setOpen(added.find((turn) => turn.blocks?.length)?.id ?? null);
        setDraft("");
      } finally {
        setPending(null);
      }
    });

  const clear = () =>
    run("clear", async () => {
      if (
        !window.confirm(
          "Start over? This conversation and all its versions are deleted.",
        )
      )
        return;
      await api.delete(`/sessions/${session.id}/export/conversation`);
      setTurns([]);
      setOpen(null);
    });

  const download = (turn: Turn) =>
    run("download", async () => {
      if (!turn.blocks?.length || !turn.templateId) return;
      const template = await api
        .get(`/export-templates/${turn.templateId}`, TemplateFileSchema)
        .catch(() => {
          throw new Error(
            `The template "${turn.templateName ?? ""}" this version was written for has been deleted.`,
          );
        });
      const bytes = await buildDocx(
        fromBase64(template.contentBase64),
        turn.blocks,
      );
      const n = versionNumber(turn);
      downloadDocx(
        bytes,
        `${session.title || "Meeting minutes"}${n > 1 ? ` v${n}` : ""}`,
      );
    });

  const working = busy !== "";
  const templateChanged =
    !!latest && !!templateId && latest.templateId !== templateId;
  const canSend =
    !working && !!templateId && !!session.noteMarkdown && !!draft.trim();
  return (
    <Dialog
      ref={dialog}
      className="w-[min(860px,calc(100vw-32px))]"
      aria-labelledby="export-title"
      onClose={onClose}
      onCancel={(event) => {
        if (working) event.preventDefault();
      }}
    >
      <div className="grid max-h-[calc(100dvh-48px)] grid-rows-[auto_minmax(0,1fr)_auto] text-[12px]">
        <header className="grid gap-2 border-b border-line p-4">
          <div className="flex items-center gap-2">
            <h2 id="export-title" className="flex-1 text-base font-bold">
              Export to Word
            </h2>
            <DialogButton
              disabled={working || turns.length === 0}
              onClick={() => void clear()}
            >
              Start over
            </DialogButton>
            <DialogButton disabled={busy === "send"} onClick={onClose}>
              Close
            </DialogButton>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[12px] font-bold">Template</span>
            <select
              aria-label="Template"
              className={fieldStyle + " min-w-0 flex-1"}
              value={templateId}
              disabled={working || templates.length === 0}
              onChange={(event) => setTemplateId(event.target.value)}
            >
              {templates.length === 0 && (
                <option value="">No templates yet: upload one</option>
              )}
              {templates.map((template) => (
                <option key={template.id} value={template.id}>
                  {template.name}
                </option>
              ))}
            </select>
            <DialogButton disabled={working} onClick={() => void upload()}>
              {busy === "upload" ? "Uploading…" : "Upload .docx…"}
            </DialogButton>
            {templateId && (
              <DialogButton
                disabled={working}
                onClick={() => void removeTemplate()}
              >
                Delete
              </DialogButton>
            )}
          </div>
          {templateChanged && (
            <p className="text-[11px] text-[#9a591c]">
              The latest version was written for another template; the next
              message writes a new first draft with this one. Earlier messages
              still apply.
            </p>
          )}
        </header>

        <div className="grid content-start gap-3 overflow-y-auto bg-surface p-4">
          {busy === "loading" && <p className="text-muted">Loading…</p>}
          {busy !== "loading" && turns.length === 0 && !pending && (
            <div className="grid justify-items-start gap-2 rounded-lg border border-dashed border-line bg-white p-4">
              <p>
                The AI rewrites these notes in the structure, layout and tone of
                your template, such as minutes you sent a client before. Then
                tell it what to change: each reply is a new version you can
                download, and this conversation is kept with the session.
              </p>
              <DialogButton
                variant="primary"
                disabled={working || !templateId || !session.noteMarkdown}
                onClick={() => void send("")}
              >
                Write the first draft
              </DialogButton>
            </div>
          )}
          {turns.map((turn) =>
            turn.role === "user" ? (
              turn.text ? (
                <div
                  key={turn.id}
                  className="max-w-[80%] justify-self-end rounded-lg bg-accent px-3 py-2 whitespace-pre-wrap text-white"
                >
                  {turn.text}
                </div>
              ) : null
            ) : (
              <div
                key={turn.id}
                className="grid max-w-full gap-2 justify-self-start rounded-lg border border-line bg-white p-3"
              >
                {turn.text && (
                  <p className="whitespace-pre-wrap">{turn.text}</p>
                )}
                {!!turn.blocks?.length && (
                  <div className="flex flex-wrap items-center gap-2">
                    <strong>Version {versionNumber(turn)}</strong>
                    <span className="text-[11px] text-muted">
                      {timeOf(turn.createdAt)} · {turn.templateName}
                      {turn.noteVersion !== session.noteVersion &&
                        " · written from earlier notes"}
                    </span>
                    <span className="flex-1" />
                    <DialogButton
                      className="py-1"
                      onClick={() => setOpen(open === turn.id ? null : turn.id)}
                    >
                      {open === turn.id ? "Hide" : "Preview"}
                    </DialogButton>
                    <DialogButton
                      className="py-1"
                      variant={turn === latest ? "primary" : "plain"}
                      disabled={working}
                      onClick={() => void download(turn)}
                    >
                      Download .docx
                    </DialogButton>
                  </div>
                )}
                {open === turn.id && turn.blocks && (
                  <Preview blocks={turn.blocks} />
                )}
              </div>
            ),
          )}
          {pending !== null && (
            <>
              {pending && (
                <div className="max-w-[80%] justify-self-end rounded-lg bg-accent px-3 py-2 whitespace-pre-wrap text-white opacity-70">
                  {pending}
                </div>
              )}
              <p className="text-muted" role="status">
                Writing{latest && !templateChanged ? " the next version" : ""}…
                this can take a minute.
              </p>
            </>
          )}
          <div ref={bottom} />
        </div>

        <footer className="grid gap-2 border-t border-line p-4">
          {error && (
            <p role="alert" className="text-red-700">
              {error}
            </p>
          )}
          <div className="flex items-end gap-2">
            <textarea
              aria-label="Message to the AI"
              className={clsx(fieldStyle, "min-h-14 flex-1 resize-y")}
              maxLength={4000}
              value={draft}
              disabled={busy === "send"}
              placeholder={
                latest
                  ? "What should change? e.g. More formal; move action items to the top; the venue was Room 3A"
                  : "Optional wishes for the first draft, e.g. leave out internal discussion"
              }
              onChange={(event) => setDraft(event.target.value)}
              onKeyDown={(event) => {
                if (
                  event.key === "Enter" &&
                  (event.metaKey || event.ctrlKey) &&
                  canSend
                ) {
                  event.preventDefault();
                  void send(draft.trim());
                }
              }}
            />
            <DialogButton
              variant="primary"
              disabled={!canSend}
              onClick={() => void send(draft.trim())}
            >
              {busy === "send" ? "Writing…" : "Send"}
            </DialogButton>
          </div>
          <small className="text-muted">
            ⌘/Ctrl + Enter sends. The template's text, these notes and your
            messages go to the note model; source links are left out. Check
            names, dates and any "[TBC]" before sending the file on.
          </small>
        </footer>
      </div>
    </Dialog>
  );
}
