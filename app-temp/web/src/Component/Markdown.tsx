import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeSanitize from "rehype-sanitize";
import mermaid from "mermaid";
import DOMPurify from "dompurify";
import { termSegments } from "./termSegments";
import { SourceCitation } from "./SourceCitation";

mermaid.initialize({
  startOnLoad: false,
  securityLevel: "strict",
  theme: "neutral",
  htmlLabels: false,
});
function Diagram({ source }: { source: string }) {
  const [svg, setSvg] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    mermaid
      .render("diagram-" + crypto.randomUUID().replaceAll("-", ""), source)
      .then((result) => {
        if (active)
          setSvg(
            DOMPurify.sanitize(result.svg, {
              USE_PROFILES: { html: true, svg: true, svgFilters: true },
            }),
          );
      })
      .catch(() => {
        if (active) setError("Diagram syntax needs review.");
      });
    return () => {
      active = false;
    };
  }, [source]);
  return (
    <figure className="flowchart">
      <figcaption>Flowchart</figcaption>
      {error ? (
        <p role="alert">{error}</p>
      ) : (
        <div
          className="flowchart-scroll"
          aria-label="Rendered flowchart"
          dangerouslySetInnerHTML={{ __html: svg }}
        />
      )}
    </figure>
  );
}

// Stable component type: activity/capture polling must not remount unchanged diagrams.
function MarkdownCode(props: { className?: string; children?: ReactNode }) {
  const language = /language-(\w+)/.exec(props.className || "")?.[1];
  const source = String(props.children).replace(/\n$/, "");
  return language === "mermaid" || language === "flowchart"
    ? <Diagram source={source} /> : <code>{props.children}</code>;
}

type MarkdownNode = { type: string; value?: string; url?: string; children?: MarkdownNode[] };

// Transform text nodes, so source-looking text inside code and existing links stays intact.
function referencePlugin({ labels, terms, groups }: { labels: Map<string, string>; terms: { id: string; term: string }[];
  groups: { id: string; transcriptIds: string[] }[] }) {
  return (tree: MarkdownNode) => {
    const groupBySource = new Map(groups.flatMap(group => group.transcriptIds.map(id => [id, group.id] as const)));
    const link = (id: string): MarkdownNode => ({ type: "link", url: groupBySource.has(id) ? `/source-group/${groupBySource.get(id)}` : `/source/${id}`,
      children: [{ type: "text", value: groupBySource.get(id) ?? labels.get(id)! }] });
    const unavailable = (): MarkdownNode => ({ type: "link", url: "/source-unavailable",
      children: [{ type: "text", value: "Source unavailable" }] });
    const highlight = (text: string): MarkdownNode[] => termSegments(text, terms).map(segment => segment.term
      ? { type: "link", url: `/term/${segment.term.id}`, children: [{ type: "text", value: segment.text }] }
      : { type: "text", value: segment.text });
    function visit(node: MarkdownNode) {
      if (!node.children || ["link", "linkReference", "code", "inlineCode"].includes(node.type)) return;
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
              else if (groups.some(group => group.id === id)) replacement.push({ type: "link", url: `/source-group/${id}`, children: [{ type: "text", value: id }] });
              else if (/^(?:[a-f0-9]{32,}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})(?:[-_][a-z0-9_-]+)?$/i.test(id))
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
      if (node.type === "paragraph" && groups.length) {
        const used = new Set<string>();
        let unresolved = false;
        const collect = (child: MarkdownNode): MarkdownNode => {
          if (child.type === "link" && child.url?.startsWith("/source-group/")) {
            used.add(child.url.slice("/source-group/".length)); return { type: "text", value: "" };
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
  groups: { id: string; transcriptIds: string[] }[];
  onSource?: (id: string) => void;
  onPlaySources?: (ids: string[]) => void;
  onReference?: (id: string) => void;
  renderTerm?: (id: string, text: string) => ReactNode;
};
const References = createContext<ReferenceContext | null>(null);

// Stable link component: player/activity refreshes must not close an open popup.
function MarkdownLink(props: { href?: string; children?: ReactNode }) {
  const { sourceLabels, groups, onSource, onPlaySources, onReference, renderTerm } = useContext(References)!;
  const set = /^\/source-(?:set|group)\/([0-9,]+)$/.exec(props.href || "");
  if (set) {
    const selected = set[1].split(",").map(id => groups.find(group => group.id === id)).filter(group => group !== undefined);
    if (selected.length) return <SourceCitation groups={selected} labels={sourceLabels} onSource={onSource} onPlay={onPlaySources} />;
  }
  const term = /^\/term\/([a-f0-9]{64})$/i.exec(props.href || "");
  if (term && renderTerm) return renderTerm(term[1], String(props.children));
  if (props.href === "/source-unavailable")
    return <span className="note-ref unavailable-ref" title="This reference does not match a source in this session. Check the original note in Edit.">Source unavailable</span>;
  const source = /^\/source\/([a-z0-9_-]+)$/i.exec(props.href || "");
  if (source)
    return onSource
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
  sources = [],
  onSource,
  terms = [],
  renderTerm,
  groups = [],
  onPlaySources,
}: {
  value: string;
  onReference?: (id: string) => void;
  sources?: { id: string; label: string }[];
  onSource?: (id: string) => void;
  terms?: { id: string; term: string }[];
  renderTerm?: (id: string, text: string) => ReactNode;
  groups?: { id: string; transcriptIds: string[] }[];
  onPlaySources?: (ids: string[]) => void;
}) {
  const sourceLabels = new Map(sources.map((source) => [source.id, source.label]));
  return (
    <References.Provider value={{ sourceLabels, groups, onSource, onPlaySources, onReference, renderTerm }}>
    <div className="markdown-preview">
      <ReactMarkdown
        remarkPlugins={[remarkGfm, [referencePlugin, { labels: sourceLabels, terms, groups }]]}
        rehypePlugins={[rehypeSanitize]}
        components={{
          code: MarkdownCode,
          a: MarkdownLink,
        }}
      >
        {value}
      </ReactMarkdown>
    </div>
    </References.Provider>
  );
}
