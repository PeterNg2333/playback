import {
  useEffect,
  useState,
  useMemo,
  memo,
  lazy,
  Suspense,
  type ReactNode,
  type ComponentType,
  type ComponentPropsWithoutRef,
} from "react";
import ReactMarkdown from "react-markdown";
import type { Components, ExtraProps } from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import { requestDiagram } from "./diagramRenderer";
import "../../styles/math.css";
function Diagram({ source }: { source: string }) {
  const [svg, setSvg] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    setSvg("");
    setError("");
    const request = requestDiagram(source);
    request.promise
      .then((result) => {
        if (active) setSvg(result);
      })
      .catch((failure: Error) => {
        if (active) setError(failure.message);
      });
    return () => {
      active = false;
      request.cancel();
    };
  }, [source]);
  return (
    <figure className="flowchart">
      <figcaption>Flowchart</figcaption>
      {error ? (
        <p role="alert">{error}</p>
      ) : !svg ? (
        <p role="status">Loading diagram…</p>
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

function FormulaSource({
  source,
  display,
  title,
}: {
  source: string;
  display: boolean;
  title: string;
}) {
  const Tag = display ? "div" : "span";
  return (
    <Tag
      className={`math-formula${display ? " math-display" : ""}`}
      data-testid="formula"
    >
      <code className="math-source" title={title}>
        {source}
      </code>
    </Tag>
  );
}
function FormulaUnavailable(props: { source: string; display: boolean }) {
  return (
    <FormulaSource
      {...props}
      title="Equation renderer could not load; source is preserved. Reload to retry."
    />
  );
}
const Formula = lazy<ComponentType<{ source: string; display: boolean }>>(() =>
  import("./MathFormula").catch(() => ({ default: FormulaUnavailable })),
);

// Stable component types: polling must not remount unchanged diagrams or formulas.
function MarkdownCode(props: { className?: string; children?: ReactNode }) {
  const language = /language-(\w+)/.exec(props.className || "")?.[1];
  const source = String(props.children).replace(/\n$/, "");
  if (language === "mermaid" || language === "flowchart")
    return <Diagram source={source} />;
  if (language === "math") {
    const display = !props.className?.split(/\s+/).includes("math-inline");
    return (
      <Suspense
        fallback={
          <FormulaSource
            source={source}
            display={display}
            title="Loading equation renderer…"
          />
        }
      >
        <Formula source={source} display={display} />
      </Suspense>
    );
  }
  return <code className={props.className}>{props.children}</code>;
}
function MarkdownPre({
  node,
  children,
}: ComponentPropsWithoutRef<"pre"> & ExtraProps) {
  const code = node?.children[0];
  const classes =
    code?.type === "element" && code.tagName === "code"
      ? code.properties.className
      : undefined;
  // Figures and block equations own their layout; normal fenced code keeps <pre>.
  return Array.isArray(classes) &&
    classes.some((x) =>
      ["language-math", "language-mermaid", "language-flowchart"].includes(
        String(x),
      ),
    ) ? (
    <>{children}</>
  ) : (
    <pre>{children}</pre>
  );
}

export type MarkdownLink = (props: {
  href?: string;
  children?: ReactNode;
}) => ReactNode;

function ExternalLink(props: { href?: string; children?: ReactNode }) {
  return (
    <a href={props.href} target="_blank" rel="noopener noreferrer">
      {props.children}
    </a>
  );
}
const textPlugins = [remarkGfm, remarkMath];
const plainComponents = {
  code: MarkdownCode,
  pre: MarkdownPre,
  a: ExternalLink,
};

// Renders Markdown with tables and lists, maths, Mermaid diagrams and sanitised HTML; links open in
// a new tab. A feature can add syntax through `plugins` and draw links through `link`; keep both
// the same across renders, or every diagram and formula is rebuilt.
export function Markdown({
  value,
  plugins,
  link,
}: {
  value: string;
  plugins?: unknown[];
  link?: MarkdownLink;
}) {
  const remarkPlugins = useMemo(
    () => (plugins ? [...textPlugins, ...plugins] : textPlugins),
    [plugins],
  );
  const components = useMemo(
    () => (link ? { ...plainComponents, a: link } : plainComponents),
    [link],
  );
  return (
    <MarkdownBody
      value={value}
      plugins={remarkPlugins}
      components={components}
    />
  );
}
const sanitizePlugins: ComponentPropsWithoutRef<
  typeof ReactMarkdown
>["rehypePlugins"] = [
  [
    rehypeSanitize,
    {
      ...defaultSchema,
      attributes: {
        ...defaultSchema.attributes,
        code: [["className", /^language-./, "math-inline", "math-display"]],
      },
    },
  ],
];
const MarkdownBody = memo(function MarkdownBody({
  value,
  plugins,
  components,
}: {
  value: string;
  plugins: unknown[];
  components: Components;
}) {
  return (
    <div className="markdown-preview" data-markdown>
      <ReactMarkdown
        remarkPlugins={plugins as never}
        rehypePlugins={sanitizePlugins}
        components={components}
      >
        {value}
      </ReactMarkdown>
    </div>
  );
});
