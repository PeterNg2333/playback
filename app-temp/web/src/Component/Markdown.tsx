import { useEffect, useState } from "react";
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

export function Markdown({ value }: { value: string }) {
  return (
    <div className="markdown-preview">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSanitize]}
        components={{
          code(props) {
            const language = /language-(\w+)/.exec(props.className || "")?.[1];
            const source = String(props.children).replace(/\n$/, "");
            return language === "mermaid" || language === "flowchart" ? (
              <Diagram source={source} />
            ) : (
              <code>{props.children}</code>
            );
          },
          a(props) {
            return (
              <a href={props.href} target="_blank" rel="noopener noreferrer">
                {props.children}
              </a>
            );
          },
        }}
      >
        {value}
      </ReactMarkdown>
    </div>
  );
}
