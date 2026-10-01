import { memo, useMemo } from "react";
import katex from "katex";
import DOMPurify from "dompurify";

type FormulaProps = { source: string; display: boolean };

// Native MathML keeps one accessible equation tree, without an HTML layout copy
// or downloaded math fonts. Each mounted formula owns its result; no global cache
// retains formulas or source references after editing or switching sessions.
export default memo(function MathFormula({ source, display }: FormulaProps) {
  const rendered = useMemo(() => {
    try {
      if (source.length > 4096)
        throw new Error(
          "Formula is too long to render; its source is preserved.",
        );
      const markup = katex.renderToString(source, {
        displayMode: display,
        output: "mathml",
        trust: false,
        throwOnError: true,
        strict: "ignore",
        maxExpand: 1000,
        maxSize: 20,
      });
      if (markup.length > 128_000)
        throw new Error(
          "Formula output limit reached; its source is preserved.",
        );
      return {
        html: DOMPurify.sanitize(markup, { USE_PROFILES: { mathMl: true } }),
      };
    } catch {
      return {
        error: "Formula cannot be rendered yet; its source is preserved.",
      };
    }
  }, [source, display]);
  const Tag = display ? "div" : "span";
  return rendered.html ? (
    <Tag
      className={`math-formula${display ? " math-display" : ""}`}
      dangerouslySetInnerHTML={{ __html: rendered.html }}
    />
  ) : (
    <Tag className={`math-formula${display ? " math-display" : ""}`}>
      <code className="math-source" title={rendered.error}>
        {source}
      </code>
    </Tag>
  );
});
