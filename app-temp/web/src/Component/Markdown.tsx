import { useEffect, useState, type ReactNode } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeSanitize from "rehype-sanitize";
import mermaid from "mermaid";
import DOMPurify from "dompurify";

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

export function Markdown({
  value,
  onReference,
  sources = [],
  onSource,
}: {
  value: string;
  onReference?: (id: string) => void;
  sources?: { id: string; label: string }[];
  onSource?: (id: string) => void;
}) {
  const sourceLabels = new Map(sources.map((source) => [source.id, source.label]));
  const sourceLink = (id: string) => `[${sourceLabels.get(id)}](/source/${id})`;
  const linkedReferences = value
    .replace(/\[ref:([a-f0-9]{64})\]/gi, (_, id: string) => `[ref](/term-ref/${id})`)
    .replace(/\[\[([^\]\n]{1,2000})\]\]/g, (match, body: string) => {
      const ids = [...body.matchAll(/[a-z0-9_-]{32,180}/gi)]
        .map((part) => part[0])
        .filter((id) => sourceLabels.has(id));
      return ids.length ? [...new Set(ids)].map(sourceLink).join(" ") : match;
    })
    .replace(/\[([^\]\n]{1,2000})\](?!\()/g, (match, body: string) => {
      const ids = [...body.matchAll(/[a-z0-9_-]{32,180}/gi)]
        .map((part) => part[0])
        .filter((id) => sourceLabels.has(id));
      return ids.length ? [...new Set(ids)].map(sourceLink).join(" ") : match;
    });
  return (
    <div className="markdown-preview">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSanitize]}
        components={{
          code: MarkdownCode,
          a(props) {
            const source = /^\/source\/([a-z0-9_-]{32,180})$/i.exec(props.href || "");
            if (source && onSource)
              return <button type="button" className="note-ref" onClick={() => onSource(source[1])} aria-label={`Jump to source at ${sourceLabels.get(source[1])}`}>{props.children}</button>;
            const reference = /^\/term-ref\/([a-f0-9]{64})$/i.exec(
              props.href || "",
            );
            if (reference && onReference)
              return (
                <button
                  type="button"
                  className="note-ref"
                  onClick={() => onReference(reference[1])}
                  aria-label="Open saved explanation"
                >
                  ref
                </button>
              );
            return (
              <a href={props.href} target="_blank" rel="noopener noreferrer">
                {props.children}
              </a>
            );
          },
        }}
      >
        {linkedReferences}
      </ReactMarkdown>
    </div>
  );
}
