import JSZip from "jszip";
import {
  DOMParser,
  XMLSerializer,
  type Document,
  type Element,
  type Node,
} from "@xmldom/xmldom";

// Reads a Word template and builds an export from it. The template's body is read as numbered elements (paragraphs with
// text and top-level tables, in document order); the AI draft names, for each block, the element whose formatting it
// copies. The exported file keeps everything else in the template: page setup, headers and footers, logos, styles,
// numbering and fonts. Only the body is replaced.

const W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
const W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
const XML = "http://www.w3.org/XML/1998/namespace";
const DOCX_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
const MAX_OUTLINE_CHARS = 24_000;

export type ExportBlock = {
  kind: "heading" | "paragraph" | "bullet" | "table";
  level: number;
  like: number;
  text: string;
  rows: { cells: string[] }[];
};

type Template = {
  zip: JSZip;
  doc: Document;
  body: Element;
  elements: Element[];
  styleNames: Map<string, string>;
};

const isW = (node: Node | null | undefined, name: string): node is Element =>
  !!node &&
  node.nodeType === 1 &&
  (node as Element).namespaceURI === W &&
  (node as Element).localName === name;

function children(element: Element, name: string): Element[] {
  const result: Element[] = [];
  for (let node = element.firstChild; node; node = node.nextSibling)
    if (isW(node, name)) result.push(node);
  return result;
}
const first = (element: Element, name: string) =>
  children(element, name)[0] as Element | undefined;
const descendants = (element: Element, name: string) =>
  Array.from(element.getElementsByTagNameNS(W, name)) as Element[];
const create = (doc: Document, name: string) =>
  doc.createElementNS(W, "w:" + name);
const attr = (element: Element | undefined, name: string) =>
  element?.getAttributeNS(W, name) ?? null;
function setAttr(element: Element, name: string, value: string) {
  element.setAttributeNS(W, "w:" + name, value);
  return element;
}

function textOf(element: Element) {
  let text = "";
  const walk = (node: Node) => {
    for (let child = node.firstChild; child; child = child.nextSibling) {
      if (isW(child, "t")) text += child.textContent ?? "";
      else if (isW(child, "tab")) text += " ";
      else if (isW(child, "br") || isW(child, "cr")) text += " ";
      else if (child.nodeType === 1) walk(child);
    }
  };
  walk(element);
  return text.replace(/\s+/g, " ").trim();
}

// Paragraphs with text outside tables, and top-level tables, in document order. Content controls and other wrappers
// are looked into; the section properties are not content.
function bodyElements(body: Element) {
  const result: Element[] = [];
  const walk = (node: Node) => {
    for (let child = node.firstChild; child; child = child.nextSibling) {
      if (isW(child, "p")) {
        if (textOf(child)) result.push(child);
      } else if (isW(child, "tbl")) result.push(child);
      else if (child.nodeType === 1 && !isW(child, "sectPr")) walk(child);
    }
  };
  walk(body);
  return result;
}

async function open(bytes: ArrayBuffer | Uint8Array): Promise<Template> {
  let zip: JSZip;
  try {
    zip = await JSZip.loadAsync(bytes);
  } catch {
    throw new Error("This file is not a Word document (.docx).");
  }
  const xml = await zip.file("word/document.xml")?.async("string");
  if (!xml) throw new Error("This file is not a Word document (.docx).");
  const doc = new DOMParser().parseFromString(xml, "text/xml");
  const body = doc.documentElement && first(doc.documentElement, "body");
  if (!body) throw new Error("This Word document has no body.");
  const styleNames = new Map<string, string>();
  const stylesXml = await zip.file("word/styles.xml")?.async("string");
  if (stylesXml) {
    const styles = new DOMParser().parseFromString(stylesXml, "text/xml");
    if (styles.documentElement)
      for (const style of children(styles.documentElement, "style")) {
        const id = attr(style, "styleId");
        const name = attr(first(style, "name"), "val");
        if (id) styleNames.set(id, name ?? id);
      }
  }
  return { zip, doc, body, elements: bodyElements(body), styleNames };
}

const paragraphStyle = (p: Element) =>
  attr(first(first(p, "pPr") ?? p, "pStyle"), "val");
const isList = (p: Element, styleNames: Map<string, string>) => {
  const pPr = first(p, "pPr");
  if (pPr && first(pPr, "numPr")) return true;
  const style = paragraphStyle(p);
  return !!style && /list/i.test(styleNames.get(style) ?? style);
};
function isBold(p: Element) {
  const run = descendants(p, "r").find((r) => textOf(r));
  const b = run && first(first(run, "rPr") ?? run, "b");
  return !!b && !["0", "false"].includes(attr(b, "val") ?? "");
}
function tableRows(table: Element) {
  return children(table, "tr").map((tr) =>
    children(tr, "tc").map((tc) => textOf(tc)),
  );
}
const clip = (text: string, length: number) =>
  text.length > length ? text.slice(0, length) + "…" : text;

// What the AI sees of a template: one line per element, and a table's first rows.
export async function readTemplate(bytes: ArrayBuffer | Uint8Array) {
  const template = await open(bytes);
  if (template.elements.length === 0)
    throw new Error("This Word document has no text to use as a template.");
  const lines: string[] = [];
  template.elements.forEach((element, index) => {
    if (isW(element, "tbl")) {
      const rows = tableRows(element);
      const columns = Math.max(...rows.map((row) => row.length), 0);
      lines.push(`[${index}] table, ${columns} columns, ${rows.length} rows:`);
      for (const row of rows.slice(0, 4))
        lines.push(
          "    | " + row.map((cell) => clip(cell, 80)).join(" | ") + " |",
        );
    } else {
      const style = paragraphStyle(element);
      const name = style ? (template.styleNames.get(style) ?? style) : "Normal";
      const marks = [
        name,
        isList(element, template.styleNames) ? "list item" : "",
        isBold(element) ? "bold" : "",
      ].filter(Boolean);
      lines.push(
        `[${index}] ${marks.join(", ")}: ${clip(textOf(element), 300)}`,
      );
    }
  });
  let outline = lines.join("\n");
  if (outline.length > MAX_OUTLINE_CHARS)
    outline = outline.slice(0, MAX_OUTLINE_CHARS) + "\n…";
  return { outline, elements: template.elements.length };
}

// A copy without the IDs and anchors that must stay unique in a document.
function cleanClone<T extends Element>(element: T): T {
  const copy = element.cloneNode(true) as T;
  const strip = (node: Element) => {
    for (const name of ["paraId", "textId"]) node.removeAttributeNS(W14, name);
    for (let child = node.firstChild; child; ) {
      const next = child.nextSibling;
      if (child.nodeType === 1) {
        const el = child as Element;
        if (
          el.namespaceURI === W &&
          [
            "commentRangeStart",
            "commentRangeEnd",
            "commentReference",
            "bookmarkStart",
            "bookmarkEnd",
            "proofErr",
            "sectPr",
          ].includes(el.localName ?? "")
        )
          node.removeChild(el);
        else strip(el);
      }
      child = next;
    }
  };
  strip(copy);
  return copy;
}

function runs(doc: Document, text: string, rPr?: Element) {
  return text.split(/\r?\n/).map((line, index) => {
    const run = create(doc, "r");
    if (rPr) run.appendChild(cleanClone(rPr));
    if (index > 0) run.appendChild(create(doc, "br"));
    const t = create(doc, "t");
    t.setAttributeNS(XML, "xml:space", "preserve");
    t.appendChild(doc.createTextNode(line));
    run.appendChild(t);
    return run;
  });
}

// A paragraph formatted like a template paragraph: its paragraph properties and the run properties of its first text.
// A template line that starts with a label in its own formatting ("Date:" in bold, then the value) keeps that split
// when the new text starts with a label too.
function paragraphLike(doc: Document, like: Element | undefined, text: string) {
  const p = create(doc, "p");
  let rPr: Element | undefined;
  let valueRPr: Element | undefined;
  let labelled = false;
  if (like) {
    const pPr = first(like, "pPr");
    if (pPr) p.appendChild(cleanClone(pPr));
    const textRuns = descendants(like, "r").filter((r) => textOf(r));
    const run = textRuns[0];
    rPr = (run && first(run, "rPr")) ?? (pPr && first(pPr, "rPr"));
    const label = run ? textOf(run) : "";
    if (textRuns.length > 1 && /[:：]$/.test(label)) {
      labelled = true;
      valueRPr = first(textRuns[1], "rPr");
    }
  }
  const split = labelled
    ? /^([^:：\n]{1,40}[:：])\s*([\s\S]*)$/.exec(text)
    : null;
  if (split) {
    for (const run of runs(doc, split[1] + " ", rPr)) p.appendChild(run);
    if (split[2])
      for (const run of runs(doc, split[2], valueRPr)) p.appendChild(run);
  } else for (const run of runs(doc, text, rPr)) p.appendChild(run);
  return p;
}

function plainParagraph(
  doc: Document,
  text: string,
  options: { styleId?: string; bold?: boolean } = {},
) {
  const p = create(doc, "p");
  if (options.styleId) {
    const pPr = create(doc, "pPr");
    pPr.appendChild(setAttr(create(doc, "pStyle"), "val", options.styleId));
    p.appendChild(pPr);
  }
  let rPr: Element | undefined;
  if (options.bold) {
    rPr = create(doc, "rPr");
    rPr.appendChild(create(doc, "b"));
  }
  for (const run of runs(doc, text, rPr)) p.appendChild(run);
  return p;
}

function grid(doc: Document, columns: number, total: number) {
  const tblGrid = create(doc, "tblGrid");
  for (let i = 0; i < columns; i++)
    tblGrid.appendChild(
      setAttr(create(doc, "gridCol"), "w", String(Math.floor(total / columns))),
    );
  return tblGrid;
}

function fillCell(
  doc: Document,
  tc: Element,
  text: string,
  keepWidth: boolean,
) {
  const tcPr = first(tc, "tcPr");
  if (tcPr)
    for (const child of Array.from(tcPr.childNodes) as Node[])
      if (
        isW(child, "gridSpan") ||
        isW(child, "vMerge") ||
        isW(child, "hMerge") ||
        (!keepWidth && isW(child, "tcW"))
      )
        tcPr.removeChild(child);
  const like = children(tc, "p")[0];
  for (const child of Array.from(tc.childNodes) as Node[])
    if (child !== tcPr) tc.removeChild(child);
  tc.appendChild(
    like ? paragraphLike(doc, like, text) : plainParagraph(doc, text),
  );
}

function rowLike(
  doc: Document,
  template: Element,
  cells: string[],
  keepWidths: boolean,
) {
  const tr = cleanClone(template);
  const tcs = children(tr, "tc");
  while (tcs.length > cells.length) tr.removeChild(tcs.pop()!);
  while (tcs.length < cells.length) {
    const cell = tcs.length
      ? cleanClone(tcs[tcs.length - 1])
      : create(doc, "tc");
    tr.appendChild(cell);
    tcs.push(cell);
  }
  tcs.forEach((tc, i) => fillCell(doc, tc, cells[i], keepWidths));
  return tr;
}

function plainTable(
  doc: Document,
  rows: string[][],
  styleNames: Map<string, string>,
) {
  const columns = rows[0].length;
  const tbl = create(doc, "tbl");
  const tblPr = create(doc, "tblPr");
  if (styleNames.has("TableGrid"))
    tblPr.appendChild(setAttr(create(doc, "tblStyle"), "val", "TableGrid"));
  else {
    const borders = create(doc, "tblBorders");
    for (const side of [
      "top",
      "left",
      "bottom",
      "right",
      "insideH",
      "insideV",
    ]) {
      const border = create(doc, side);
      setAttr(border, "val", "single");
      setAttr(border, "sz", "4");
      setAttr(border, "space", "0");
      setAttr(border, "color", "auto");
      borders.appendChild(border);
    }
    tblPr.appendChild(borders);
  }
  const width = create(doc, "tblW");
  setAttr(width, "w", "5000");
  setAttr(width, "type", "pct");
  tblPr.appendChild(width);
  tbl.appendChild(tblPr);
  tbl.appendChild(grid(doc, columns, 9000));
  rows.forEach((cells, index) => {
    const tr = create(doc, "tr");
    for (const cell of cells) {
      const tc = create(doc, "tc");
      tc.appendChild(plainParagraph(doc, cell, { bold: index === 0 }));
      tr.appendChild(tc);
    }
    tbl.appendChild(tr);
  });
  return tbl;
}

// A table formatted like a template table: its table properties, its header row for the first row and its next row for
// every other row. A different number of columns spreads the template's width evenly.
function tableLike(doc: Document, like: Element, rows: string[][]) {
  const columns = rows[0].length;
  const tbl = create(doc, "tbl");
  const tblPr = first(like, "tblPr");
  if (tblPr) tbl.appendChild(cleanClone(tblPr));
  const tblGrid = first(like, "tblGrid");
  const gridCols = tblGrid ? children(tblGrid, "gridCol") : [];
  const keepWidths = gridCols.length === columns;
  if (tblGrid && keepWidths) tbl.appendChild(cleanClone(tblGrid));
  else {
    const total = gridCols.reduce(
      (sum, col) => sum + (Number(attr(col, "w")) || 0),
      0,
    );
    tbl.appendChild(grid(doc, columns, total || 9000));
  }
  const templateRows = children(like, "tr");
  const header = templateRows[0];
  const body = templateRows[1] ?? templateRows[0];
  rows.forEach((cells, index) =>
    tbl.appendChild(
      header
        ? rowLike(doc, index === 0 ? header : body, cells, keepWidths)
        : create(doc, "tr"),
    ),
  );
  return tbl;
}

// The exported document: the template with its body replaced by the draft's blocks.
export async function buildDocx(
  templateBytes: ArrayBuffer | Uint8Array,
  blocks: ExportBlock[],
) {
  const template = await open(templateBytes);
  const { doc, body, elements, styleNames } = template;
  const paragraphs = elements.filter((e) => isW(e, "p"));
  const styleIdNamed = (pattern: RegExp) =>
    [...styleNames].find(([, name]) => pattern.test(name))?.[0];
  const fallbackList = paragraphs.find((p) => isList(p, styleNames));
  const fallbackBody = paragraphs.find((p) => {
    const style = paragraphStyle(p);
    const name = style ? (styleNames.get(style) ?? style) : "Normal";
    return !isList(p, styleNames) && !/heading|title|caption|toc/i.test(name);
  });
  const fallbackTable = elements.find((e) => isW(e, "tbl"));

  const sectPr = children(body, "sectPr").pop() ?? null;
  for (const child of Array.from(body.childNodes) as Node[])
    if (child !== sectPr) body.removeChild(child);
  const add = (node: Element) => body.insertBefore(node, sectPr);

  let previousTable = false;
  for (const block of blocks) {
    const like = block.like >= 0 ? elements[block.like] : undefined;
    if (block.kind === "table") {
      const width = Math.max(1, ...block.rows.map((row) => row.cells.length));
      const rows = block.rows
        .map((row) =>
          Array.from({ length: width }, (_, i) => row.cells[i] ?? ""),
        )
        .filter((row) => row.some((cell) => cell.trim()));
      if (rows.length === 0) continue;
      if (previousTable) add(create(doc, "p")); // Two tables in a row would merge into one.
      const tableTemplate = isW(like, "tbl") ? like : fallbackTable;
      add(
        tableTemplate
          ? tableLike(doc, tableTemplate, rows)
          : plainTable(doc, rows, styleNames),
      );
      previousTable = true;
      continue;
    }
    previousTable = false;
    const text = block.text.trim();
    if (!text) continue;
    const paragraphTemplate = isW(like, "p") ? like : undefined;
    if (block.kind === "heading") {
      const level = Math.min(Math.max(block.level || 1, 1), 3);
      const styleId = styleIdNamed(new RegExp(`^heading ${level}$`, "i"));
      add(
        paragraphTemplate
          ? paragraphLike(doc, paragraphTemplate, text)
          : plainParagraph(doc, text, styleId ? { styleId } : { bold: true }),
      );
    } else if (block.kind === "bullet") {
      const listTemplate =
        paragraphTemplate && isList(paragraphTemplate, styleNames)
          ? paragraphTemplate
          : fallbackList;
      add(
        listTemplate
          ? paragraphLike(doc, listTemplate, text)
          : paragraphLike(doc, fallbackBody, "• " + text),
      );
    } else add(paragraphLike(doc, paragraphTemplate ?? fallbackBody, text));
  }
  // A document body needs a paragraph after a final table.
  if (previousTable) add(create(doc, "p"));

  template.zip.file(
    "word/document.xml",
    new XMLSerializer().serializeToString(doc),
  );
  return template.zip.generateAsync({
    type: "uint8array",
    compression: "DEFLATE",
    mimeType: DOCX_TYPE,
  });
}

export function downloadDocx(bytes: Uint8Array, fileName: string) {
  const url = URL.createObjectURL(
    new Blob([bytes as BlobPart], { type: DOCX_TYPE }),
  );
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName.replace(/[\\/:*?"<>|]+/g, " ").trim() + ".docx";
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
