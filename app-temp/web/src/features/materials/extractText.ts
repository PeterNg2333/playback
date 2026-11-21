import JSZip from "jszip";
import {
  DOMParser,
  type Document,
  type Element,
  type Node,
} from "@xmldom/xmldom";

// Turns an uploaded file into the plain text a session keeps as material. Word, PowerPoint, Excel and PDF files are read
// here, in the browser; only the text reaches the API. Headings, bold phrases and tables are kept in Markdown form, so
// the term finder and the speech-recognition vocabulary can pick out the names and terms they mark.

export const MATERIAL_ACCEPT =
  ".txt,.md,.csv,.docx,.pptx,.xlsx,.pdf,text/plain,text/markdown,text/csv," +
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document," +
  "application/vnd.openxmlformats-officedocument.presentationml.presentation," +
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/pdf";
export const MAX_MATERIAL_CHARS = 250_000;
const MAX_FILE_BYTES = 30_000_000;
const MAX_SHEET_ROWS = 2_000;

const W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
const A = "http://schemas.openxmlformats.org/drawingml/2006/main";
const S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
const R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
const REL = "http://schemas.openxmlformats.org/package/2006/relationships";

const parse = (xml: string) =>
  new DOMParser().parseFromString(xml, "text/xml") as Document;
const all = (node: Document | Element, ns: string, name: string) =>
  Array.from(node.getElementsByTagNameNS(ns, name)) as Element[];
const is = (node: Node | null, ns: string, name: string): node is Element =>
  !!node &&
  node.nodeType === 1 &&
  (node as Element).namespaceURI === ns &&
  (node as Element).localName === name;
const clean = (text: string) => text.replace(/[ \t ]+/g, " ").trim();

async function zipOf(bytes: ArrayBuffer, kind: string) {
  try {
    return await JSZip.loadAsync(bytes);
  } catch {
    throw new Error(`This file could not be opened as ${kind}.`);
  }
}
async function xmlIn(zip: JSZip, path: string) {
  const text = await zip.file(path)?.async("string");
  return text ? parse(text) : null;
}
// The targets of a part's relationships, by relationship ID, as paths inside the package.
async function relationships(zip: JSZip, part: string) {
  const slash = part.lastIndexOf("/");
  const folder = part.slice(0, slash);
  const doc = await xmlIn(zip, `${folder}/_rels/${part.slice(slash + 1)}.rels`);
  const result = new Map<string, string>();
  if (!doc) return result;
  for (const rel of all(doc, REL, "Relationship")) {
    const target = rel.getAttribute("Target") ?? "";
    const path = target.startsWith("/")
      ? target.slice(1)
      : new URL(target, `https://x/${folder}/`).pathname.slice(1);
    result.set(rel.getAttribute("Id") ?? "", path);
  }
  return result;
}

// ── Word ──

function wordParagraph(p: Element, styleNames: Map<string, string>) {
  let text = "";
  for (const run of all(p, W, "r")) {
    let piece = "";
    for (let child = run.firstChild; child; child = child.nextSibling) {
      if (is(child, W, "t")) piece += child.textContent ?? "";
      else if (is(child, W, "tab")) piece += " ";
      else if (is(child, W, "br") || is(child, W, "cr")) piece += "\n";
    }
    const rPr = all(run, W, "rPr")[0];
    const bold =
      rPr &&
      all(rPr, W, "b").some(
        (b) => !["0", "false"].includes(b.getAttributeNS(W, "val") ?? ""),
      );
    const trimmed = piece.trim();
    text +=
      bold &&
      trimmed.length >= 3 &&
      trimmed.length <= 80 &&
      !piece.includes("\n")
        ? piece.replace(trimmed, `**${trimmed}**`)
        : piece;
  }
  text = text.replace(/\*\*\s*\*\*/g, "");
  if (!clean(text)) return "";
  const pPr = all(p, W, "pPr")[0];
  const style = pPr && all(pPr, W, "pStyle")[0]?.getAttributeNS(W, "val");
  const name = (style && styleNames.get(style)) || style || "";
  const heading = /^(?:heading\s*(\d)|title)$/i.exec(name);
  if (heading)
    return (
      "#".repeat(Math.min(Number(heading[1] ?? 1), 4)) +
      " " +
      clean(text.replace(/\*\*/g, ""))
    );
  if ((pPr && all(pPr, W, "numPr").length) || /list/i.test(name))
    return "- " + clean(text);
  return text.split("\n").map(clean).filter(Boolean).join("\n");
}

function wordTable(table: Element, styleNames: Map<string, string>) {
  const rows: string[] = [];
  for (let tr = table.firstChild; tr; tr = tr.nextSibling) {
    if (!is(tr, W, "tr")) continue;
    const cells: string[] = [];
    for (let tc = tr.firstChild; tc; tc = tc.nextSibling)
      if (is(tc, W, "tc"))
        cells.push(
          all(tc, W, "p")
            .map((p) => wordParagraph(p, styleNames))
            .filter(Boolean)
            .join(" ")
            .replace(/\|/g, "/")
            .replace(/\s+/g, " "),
        );
    if (cells.some(Boolean)) rows.push("| " + cells.join(" | ") + " |");
  }
  return rows.join("\n");
}

async function fromDocx(bytes: ArrayBuffer) {
  const zip = await zipOf(bytes, "a Word document");
  const doc = await xmlIn(zip, "word/document.xml");
  if (!doc) throw new Error("This file is not a Word document (.docx).");
  const styleNames = new Map<string, string>();
  const styles = await xmlIn(zip, "word/styles.xml");
  if (styles)
    for (const style of all(styles, W, "style")) {
      const id = style.getAttributeNS(W, "styleId");
      const name = all(style, W, "name")[0]?.getAttributeNS(W, "val");
      if (id) styleNames.set(id, name ?? id);
    }
  const body = all(doc, W, "body")[0];
  const blocks: string[] = [];
  const walk = (node: Node) => {
    for (let child = node.firstChild; child; child = child.nextSibling) {
      if (is(child, W, "p")) blocks.push(wordParagraph(child, styleNames));
      else if (is(child, W, "tbl")) blocks.push(wordTable(child, styleNames));
      else if (child.nodeType === 1 && !is(child, W, "sectPr")) walk(child);
    }
  };
  if (body) walk(body);
  return blocks.filter(Boolean).join("\n\n");
}

// ── PowerPoint ──

function slideText(doc: Document) {
  return all(doc, A, "p")
    .map((p) =>
      clean(
        all(p, A, "t")
          .map((t) => t.textContent ?? "")
          .join(""),
      ),
    )
    .filter(Boolean);
}

async function fromPptx(bytes: ArrayBuffer) {
  const zip = await zipOf(bytes, "a PowerPoint file");
  const presentation = await xmlIn(zip, "ppt/presentation.xml");
  if (!presentation)
    throw new Error("This file is not a PowerPoint file (.pptx).");
  const rels = await relationships(zip, "ppt/presentation.xml");
  const P = "http://schemas.openxmlformats.org/presentationml/2006/main";
  const slides = all(presentation, P, "sldId")
    .map((s) => rels.get(s.getAttributeNS(R, "id") ?? ""))
    .filter((path): path is string => !!path);
  const parts: string[] = [];
  for (const [index, path] of slides.entries()) {
    const slide = await xmlIn(zip, path);
    if (!slide) continue;
    const [title, ...rest] = slideText(slide);
    const lines = [
      `## Slide ${index + 1}${title ? ": " + title : ""}`,
      ...rest.map((x) => "- " + x),
    ];
    const notesPath = [...(await relationships(zip, path)).values()].find((x) =>
      /notesSlide/i.test(x),
    );
    const notes = notesPath ? await xmlIn(zip, notesPath) : null;
    const spoken = notes
      ? slideText(notes).filter((x) => !/^\d+$/.test(x))
      : [];
    if (spoken.length) lines.push("Speaker notes: " + spoken.join(" "));
    parts.push(lines.join("\n"));
  }
  return parts.join("\n\n");
}

// ── Excel ──

async function fromXlsx(bytes: ArrayBuffer) {
  const zip = await zipOf(bytes, "an Excel workbook");
  const workbook = await xmlIn(zip, "xl/workbook.xml");
  if (!workbook) throw new Error("This file is not an Excel workbook (.xlsx).");
  const shared: string[] = [];
  const strings = await xmlIn(zip, "xl/sharedStrings.xml");
  if (strings)
    for (const si of all(strings, S, "si"))
      shared.push(
        all(si, S, "t")
          .map((t) => t.textContent ?? "")
          .join(""),
      );
  const rels = await relationships(zip, "xl/workbook.xml");
  const parts: string[] = [];
  for (const sheet of all(workbook, S, "sheet")) {
    const path = rels.get(sheet.getAttributeNS(R, "id") ?? "");
    const doc = path ? await xmlIn(zip, path) : null;
    if (!doc) continue;
    const rows: string[] = [];
    for (const row of all(doc, S, "row")) {
      if (rows.length >= MAX_SHEET_ROWS) break;
      const cells = all(row, S, "c").map((c) => {
        const type = c.getAttribute("t");
        const value = all(c, S, "v")[0]?.textContent ?? "";
        const text =
          type === "s"
            ? (shared[Number(value)] ?? "")
            : type === "inlineStr"
              ? all(c, S, "t")
                  .map((t) => t.textContent ?? "")
                  .join("")
              : value;
        return clean(text).replace(/\|/g, "/");
      });
      if (cells.some(Boolean)) rows.push("| " + cells.join(" | ") + " |");
    }
    if (rows.length)
      parts.push(
        `## Sheet: ${sheet.getAttribute("name") ?? ""}\n` + rows.join("\n"),
      );
  }
  return parts.join("\n\n");
}

// ── PDF ──

async function fromPdf(bytes: ArrayBuffer) {
  const pdfjs = await import("pdfjs-dist");
  const worker = await import("pdfjs-dist/build/pdf.worker.min.mjs?url");
  pdfjs.GlobalWorkerOptions.workerSrc = worker.default;
  let pdf;
  try {
    pdf = await pdfjs.getDocument({ data: new Uint8Array(bytes) }).promise;
  } catch {
    throw new Error(
      "This PDF could not be opened. It may be damaged or password-protected.",
    );
  }
  const pages: string[] = [];
  for (let number = 1; number <= pdf.numPages; number++) {
    const page = await pdf.getPage(number);
    const content = await page.getTextContent();
    let text = "";
    for (const item of content.items)
      if ("str" in item) text += item.str + (item.hasEOL ? "\n" : "");
    const lines = text.split("\n").map(clean).filter(Boolean);
    if (lines.length) pages.push(`## Page ${number}\n` + lines.join("\n"));
  }
  await pdf.destroy();
  if (pages.length === 0)
    throw new Error(
      "No text was found in this PDF. A scanned PDF holds pictures of pages, which cannot be read here.",
    );
  return pages.join("\n\n");
}

// The text of a file, at most MAX_MATERIAL_CHARS; truncated says whether the end was left out.
export async function extractMaterialText(file: File) {
  if (file.size > MAX_FILE_BYTES)
    throw new Error("Choose a file of up to 30 MB.");
  const extension = /\.([a-z0-9]+)$/i.exec(file.name)?.[1]?.toLowerCase() ?? "";
  let text: string;
  if (extension === "docx") text = await fromDocx(await file.arrayBuffer());
  else if (extension === "pptx")
    text = await fromPptx(await file.arrayBuffer());
  else if (extension === "xlsx")
    text = await fromXlsx(await file.arrayBuffer());
  else if (extension === "pdf") text = await fromPdf(await file.arrayBuffer());
  else if (
    ["txt", "md", "csv", "markdown", ""].includes(extension) ||
    file.type.startsWith("text/")
  )
    text = await file.text();
  else
    throw new Error(
      "Choose a .txt, .md, .csv, .docx, .pptx, .xlsx or .pdf file. Older .doc, .ppt and .xls files need saving in the newer format first.",
    );
  text = text
    .replace(/\r\n?/g, "\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
  if (!text) throw new Error("No text was found in this file.");
  const truncated = text.length > MAX_MATERIAL_CHARS;
  return {
    text: truncated ? text.slice(0, MAX_MATERIAL_CHARS) : text,
    truncated,
  };
}
