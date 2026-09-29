import { createContext, useContext, useEffect, useState, useMemo, memo, lazy, Suspense, type ReactNode, type ComponentType, type ComponentPropsWithoutRef } from "react";
import ReactMarkdown from "react-markdown";
import type { ExtraProps } from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import { termSegments } from "./termSegments";
import { SourceCitation } from "./SourceCitation";
import { requestDiagram } from "./diagramRenderer";
import "../styles/math.css";
function Diagram({ source }: { source: string }) {
  const [svg, setSvg] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    setSvg(""); setError("");
    const request = requestDiagram(source);
    request.promise.then(result => { if (active) setSvg(result); })
      .catch((failure: Error) => {
        if (active) setError(failure.message);
      });
    return () => {
      active = false; request.cancel();
    };
  }, [source]);
  return (
    <figure className="flowchart">
      <figcaption>Flowchart</figcaption>
      {error ? (
        <p role="alert">{error}</p>
      ) : !svg ? <p role="status">Loading diagram…</p> : (
        <div
          className="flowchart-scroll"
          aria-label="Rendered flowchart"
          dangerouslySetInnerHTML={{ __html: svg }}
        />
      )}
    </figure>
  );
}

function FormulaSource({ source, display, title }: { source: string; display: boolean; title: string }) {
  const Tag = display ? "div" : "span";
  return <Tag className={`math-formula${display ? " math-display" : ""}`}><code className="math-source" title={title}>{source}</code></Tag>;
}
function FormulaUnavailable(props: { source: string; display: boolean }) {
  return <FormulaSource {...props} title="Equation renderer could not load; source is preserved. Reload to retry." />;
}
const Formula = lazy<ComponentType<{ source: string; display: boolean }>>(() => import("./MathFormula").catch(() => ({ default: FormulaUnavailable })));

// Stable component types: polling must not remount unchanged diagrams or formulas.
function MarkdownCode(props: { className?: string; children?: ReactNode }) {
  const language = /language-(\w+)/.exec(props.className || "")?.[1];
  const source = String(props.children).replace(/\n$/, "");
  if (language === "mermaid" || language === "flowchart") return <Diagram source={source} />;
  if (language === "math") {
    const display = !props.className?.split(/\s+/).includes("math-inline");
    return <Suspense fallback={<FormulaSource source={source} display={display} title="Loading equation renderer…" />}>
      <Formula source={source} display={display} />
    </Suspense>;
  }
  return <code className={props.className}>{props.children}</code>;
}
function MarkdownPre({ node, children }: ComponentPropsWithoutRef<"pre"> & ExtraProps) {
  const code = node?.children[0];
  const classes = code?.type === "element" && code.tagName === "code" ? code.properties.className : undefined;
  // Figures and block equations own their layout; normal fenced code keeps <pre>.
  return Array.isArray(classes) && classes.some(x => ["language-math", "language-mermaid", "language-flowchart"].includes(String(x)))
    ? <>{children}</> : <pre>{children}</pre>;
}

type MarkdownNode = { type: string; value?: string; url?: string; children?: MarkdownNode[] };

// Transform text nodes, so source-looking text inside code and existing links stays intact.
function referencePlugin({ references, terms }: { references: MarkdownReferences; terms: { id: string; term: string }[] }) {
  return (tree: MarkdownNode) => {
    const { sourceLabels: labels, groupBySource, groupsById } = references;
    const link = (id: string): MarkdownNode => ({ type: "link", url: `/source/${id}`,
      children: [{ type: "text", value: groupBySource.get(id) ?? labels.get(id)! }] });
    const unavailable = (): MarkdownNode => ({ type: "link", url: "/source-unavailable",
      children: [{ type: "text", value: "Source unavailable" }] });
    const highlight = (text: string): MarkdownNode[] => termSegments(text, terms).map(segment => segment.term
      ? { type: "link", url: `/term/${segment.term.id}`, children: [{ type: "text", value: segment.text }] }
      : { type: "text", value: segment.text });
    function visit(node: MarkdownNode) {
      if (!node.children || ["link", "linkReference", "code", "inlineCode", "math", "inlineMath"].includes(node.type)) return;
      node.children = node.children.flatMap(child => {
        if (child.type !== "text" || !child.value) { visit(child); return [child]; }
        const result: MarkdownNode[] = [];
        let cursor = 0;
        for (const match of child.value.matchAll(/\[\[([^\]\n]{1,16000})\]\]|\[([^\]\n]{1,16000})\]/g)) {
          const body = match[1] ?? match[2];
          let replacement: MarkdownNode[] = [];
          if (/^ref:/i.test(body)) {
            const id = body.slice(4);
            replacement = /^[a-f0-9]{64}$/i.test(id)
              ? [{ type: "link", url: `/term-ref/${id}`, children: [{ type: "text", value: "Explanation" }] }]
              : [unavailable()];
          } else {
            const tokens = [...new Set(body.match(/[a-z0-9_-]+/gi) ?? [])];
            for (const id of tokens) {
              if (labels.has(id)) replacement.push(link(id));
              else if (groupsById.has(id)) replacement.push({ type: "link", url: `/source-group/${id}`, children: [{ type: "text", value: id }] });
              else if (/^(?:cite_[a-f0-9]+|(?:[a-f0-9]{32,}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})(?:[-_][a-z0-9_-]+)?)$/i.test(id))
                replacement.push(unavailable());
            }
          }
          if (!replacement.length) continue;
          if (match.index! > cursor) result.push({ type: "text", value: child.value.slice(cursor, match.index) });
          replacement.forEach((item, index) => {
            if (index) result.push({ type: "text", value: " " });
            result.push(item);
          });
          cursor = match.index! + match[0].length;
        }
        if (!cursor) return highlight(child.value);
        if (cursor < child.value.length) result.push({ type: "text", value: child.value.slice(cursor) });
        return result.flatMap(part => part.type === "text" ? highlight(part.value ?? "") : [part]);
      });
      if (node.type === "paragraph") {
        const used = new Set<string>();
        let unresolved = false;
        const collect = (child: MarkdownNode): MarkdownNode => {
          if (child.type === "link" && child.url?.startsWith("/source-group/")) {
            groupsById.get(child.url.slice("/source-group/".length))?.transcriptIds.forEach(id => used.add(id)); return { type: "text", value: "" };
          }
          if (child.type === "link" && child.url?.startsWith("/source/") && labels.has(child.url.slice(8))) {
            used.add(child.url.slice(8)); return { type: "text", value: "" };
          }
          if (child.type === "link" && child.url === "/source-unavailable") {
            unresolved = true; return { type: "text", value: "" };
          }
          if (child.children) child.children = child.children.map(collect);
          return child;
        };
        node.children = node.children.map(collect);
        if (used.size) node.children.push({ type: "text", value: " " }, { type: "link", url: `/source-set/${[...used].join(",")}`,
          children: [{ type: "text", value: "Sources" }] });
        if (unresolved) node.children.push({ type: "text", value: " " }, unavailable());
      }
    }
    visit(tree);
  };
}

type ReferenceContext = {
  sourceLabels: Map<string, string>;
  reading?: boolean;
  sourceDetails: Map<string, { startMs?: number; endMs?: number; sourceId?: string; text?: string }>;
  groups: { id: string; transcriptIds: string[] }[];
  groupsById: Map<string, { id: string; transcriptIds: string[] }>;
  onSource?: (id: string) => void;
  onPlaySources?: (ids: string[]) => void;
  onReference?: (id: string) => void;
  renderTerm?: (id: string, text: string) => ReactNode;
};
const References = createContext<ReferenceContext | null>(null);
const noSources: { id: string; label: string; startMs?: number; endMs?: number; sourceId?: string; text?: string }[] = [];
const noTerms: { id: string; term: string }[] = [];
const noGroups: { id: string; transcriptIds: string[] }[] = [];
export type MarkdownReferences = Pick<ReferenceContext, "sourceLabels" | "sourceDetails" | "groups" | "groupsById"> & { groupBySource: Map<string, string> };
export function indexMarkdownSources(sources: typeof noSources, groups: typeof noGroups): MarkdownReferences {
  return { sourceLabels: new Map(sources.map(x => [x.id, x.label])), sourceDetails: new Map(sources.map(x => [x.id, x])), groups,
    groupsById: new Map(groups.map(x => [x.id, x])), groupBySource: new Map(groups.flatMap(group => group.transcriptIds.map(id => [id, group.id] as const))) };
}

// Stable link component: player/activity refreshes must not close an open popup.
function MarkdownLink(props: { href?: string; children?: ReactNode }) {
  const { sourceLabels, sourceDetails, reading, groupsById, onSource, onPlaySources, onReference, renderTerm } = useContext(References)!;
  const set = /^\/source-(set|group)\/([a-z0-9_,\-]+)$/i.exec(props.href || "");
  if (set) {
    if (reading) return null;
    const ids = set[1] === "group" ? groupsById.get(set[2])?.transcriptIds ?? [] : set[2].split(",");
    if (ids.length) return <SourceCitation groups={[{ id: set[2], transcriptIds: [...new Set(ids)] }]} labels={sourceLabels}
      details={sourceDetails} onSource={onSource} onPlay={onPlaySources} />;
  }
  const term = /^\/term\/([a-f0-9]{64})$/i.exec(props.href || "");
  if (term && renderTerm) return renderTerm(term[1], String(props.children));
  if (props.href === "/source-unavailable")
    return <span className="note-ref unavailable-ref" title="This reference does not match a source in this session. Check the original note in Edit.">Source unavailable</span>;
  const source = /^\/source\/([a-z0-9_-]+)$/i.exec(props.href || "");
  if (source)
    return reading ? null : onSource
      ? <button type="button" className="note-ref" onClick={() => onSource(source[1])} aria-label={`Jump to source at ${sourceLabels.get(source[1])}`}>{props.children}</button>
      : <span className="note-ref">{props.children}</span>;
  const reference = /^\/term-ref\/([a-f0-9]{64})$/i.exec(props.href || "");
  if (reference && onReference)
    return <button type="button" className="note-ref" onClick={() => onReference(reference[1])} aria-label="Open saved explanation">Explanation</button>;
  if (reference) return <span className="note-ref">Explanation</span>;
  return <a href={props.href} target="_blank" rel="noopener noreferrer">{props.children}</a>;
}

export function Markdown({
  value,
  onReference,
  sources = noSources,
  onSource,
  terms = noTerms,
  renderTerm,
  groups = noGroups,
  onPlaySources,
  reading = false,
  references,
}: {
  value: string;
  onReference?: (id: string) => void;
  sources?: { id: string; label: string; startMs?: number; endMs?: number; sourceId?: string; text?: string }[];
  reading?: boolean;
  references?: MarkdownReferences;
  onSource?: (id: string) => void;
  terms?: { id: string; term: string }[];
  renderTerm?: (id: string, text: string) => ReactNode;
  groups?: { id: string; transcriptIds: string[] }[];
  onPlaySources?: (ids: string[]) => void;
}) {
  const indexed = useMemo(() => references ?? indexMarkdownSources(sources, groups), [references, sources, groups]);
  const plugins = useMemo(() => [remarkGfm, remarkMath, [referencePlugin, { references: indexed, terms }]], [indexed, terms]);
  const context = useMemo(() => ({ ...indexed, reading, onSource, onPlaySources, onReference, renderTerm }),
    [indexed, reading, onSource, onPlaySources, onReference, renderTerm]);
  return (
    <References.Provider value={context}>
      <MarkdownBody value={value} plugins={plugins} />
    </References.Provider>
  );
}
const components = { code: MarkdownCode, pre: MarkdownPre, a: MarkdownLink };
const sanitizePlugins: ComponentPropsWithoutRef<typeof ReactMarkdown>["rehypePlugins"] = [[rehypeSanitize, { ...defaultSchema, attributes: {
  ...defaultSchema.attributes, code: [["className", /^language-./, "math-inline", "math-display"]],
} }]];
const MarkdownBody = memo(function MarkdownBody({ value, plugins }: { value: string; plugins: unknown[] }) {
  return <div className="markdown-preview"><ReactMarkdown remarkPlugins={plugins as never} rehypePlugins={sanitizePlugins} components={components}>{value}</ReactMarkdown></div>;
});
