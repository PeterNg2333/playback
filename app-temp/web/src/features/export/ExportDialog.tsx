import { useEffect, useRef, useState } from "react";
import { z } from "zod";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { api } from "../../lib/backend/client";
import type { Session } from "../../lib/backend/schemas";
import {
  buildDocx,
  downloadDocx,
  readTemplate,
  type ExportBlock,
} from "./docxTemplate";

// Export to Word: pick (or upload) a Word template, let the AI write this meeting's document in the template's structure
// and tone, check the preview, then download a .docx that keeps the template's layout, styles, headers and footers.

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
const DraftSchema = z.object({
  blocks: z.array(BlockSchema),
  provider: z.string(),
  model: z.string(),
});
type Draft = z.infer<typeof DraftSchema>;

const MAX_TEMPLATE_BYTES = 5_000_000;
const labelStyle = "grid gap-1 text-[12px] font-bold";
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
    <div
      className="grid max-h-[46dvh] gap-2 overflow-y-auto rounded-lg border border-line bg-white p-4 text-[12px] leading-[1.5]"
      data-testid="export-preview"
    >
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
  const [templates, setTemplates] = useState<z.infer<typeof TemplatesSchema>>(
    [],
  );
  const [templateId, setTemplateId] = useState("");
  const [instruction, setInstruction] = useState("");
  const [draft, setDraft] = useState<Draft | null>(null);
  const [busy, setBusy] = useState<
    "" | "loading" | "upload" | "draft" | "download" | "delete"
  >("loading");
  const [error, setError] = useState("");

  async function loadTemplates(select?: string) {
    const list = await api.get("/export-templates", TemplatesSchema);
    setTemplates(list);
    setTemplateId(
      (current) =>
        select ??
        (list.some((x) => x.id === current) ? current : (list[0]?.id ?? "")),
    );
  }
  useEffect(() => {
    dialog.current?.showModal();
    loadTemplates()
      .catch((failure) => setError(message(failure)))
      .finally(() => setBusy(""));
  }, []);

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
      setDraft(null);
      await loadTemplates(saved.id);
    });

  const remove = () =>
    run("delete", async () => {
      const template = templates.find((x) => x.id === templateId);
      if (
        !template ||
        !window.confirm(`Delete the template "${template.name}"?`)
      )
        return;
      await api.delete(`/export-templates/${template.id}`);
      setDraft(null);
      await loadTemplates("");
    });

  const generate = () =>
    run("draft", async () => {
      setDraft(
        await api.post(
          `/sessions/${session.id}/export/draft`,
          { templateId, instruction: instruction.trim() || null },
          DraftSchema,
        ),
      );
    });

  const download = () =>
    run("download", async () => {
      if (!draft) return;
      const template = await api.get(
        `/export-templates/${templateId}`,
        TemplateFileSchema,
      );
      const bytes = await buildDocx(
        fromBase64(template.contentBase64),
        draft.blocks,
      );
      downloadDocx(bytes, session.title || "Meeting minutes");
    });

  const working = busy !== "";
  return (
    <Dialog
      ref={dialog}
      className="w-[min(760px,calc(100vw-32px))]"
      aria-labelledby="export-title"
      onClose={onClose}
      onCancel={(event) => {
        if (working) event.preventDefault();
      }}
    >
      <div className="grid gap-3 p-5 text-[12px]">
        <h2 id="export-title" className="text-base font-bold">
          Export to Word
        </h2>
        <p className="text-muted">
          The AI rewrites these notes in the structure, layout and tone of a
          Word template, such as minutes you sent a client before. The exported
          file keeps the template's page setup, headers, footers, logo and
          styles.
        </p>
        <div className={labelStyle}>
          Template
          <div className="flex flex-wrap gap-2">
            <select
              aria-label="Template"
              className={fieldStyle + " min-w-0 flex-1"}
              value={templateId}
              disabled={working || templates.length === 0}
              onChange={(event) => {
                setTemplateId(event.target.value);
                setDraft(null);
              }}
            >
              {templates.length === 0 && (
                <option value="">No templates yet</option>
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
              <DialogButton disabled={working} onClick={() => void remove()}>
                Delete
              </DialogButton>
            )}
          </div>
        </div>
        <label className={labelStyle}>
          Extra instructions (optional)
          <textarea
            className={fieldStyle + " min-h-16"}
            maxLength={2000}
            value={instruction}
            disabled={working}
            placeholder="e.g. More formal; leave out internal discussion; write in English"
            onChange={(event) => setInstruction(event.target.value)}
          />
        </label>
        {draft && (
          <div className="grid gap-1">
            <strong className="text-[12px]">Preview</strong>
            <Preview blocks={draft.blocks} />
            <small className="text-muted">
              Written by {draft.provider} · {draft.model}. Check names, dates
              and any "[TBC]" before sending; the Word file is formatted like
              the template.
            </small>
          </div>
        )}
        <p className="text-muted">
          The template's text and these notes are sent to the note model. Source
          links are left out of the export.
        </p>
        {error && (
          <p role="alert" className="text-red-700">
            {error}
          </p>
        )}
        <DialogActions>
          <DialogButton disabled={working} onClick={onClose}>
            Close
          </DialogButton>
          <DialogButton
            disabled={working || !templateId || !session.noteMarkdown}
            onClick={() => void generate()}
          >
            {busy === "draft"
              ? "Writing…"
              : draft
                ? "Write again"
                : "Generate preview"}
          </DialogButton>
          <DialogButton
            variant="primary"
            disabled={working || !draft}
            onClick={() => void download()}
          >
            {busy === "download" ? "Building…" : "Download .docx"}
          </DialogButton>
        </DialogActions>
      </div>
    </Dialog>
  );
}
