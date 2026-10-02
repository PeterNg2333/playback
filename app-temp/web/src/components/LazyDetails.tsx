import { useState, type ComponentPropsWithoutRef, type ReactNode } from "react";

// Native <details> hides its children but still mounts their entire DOM/React tree.
// Construct the expensive body only while expanded and release it on collapse.
export function LazyDetails({
  summary,
  summaryClassName,
  children,
  ...props
}: Omit<
  ComponentPropsWithoutRef<"details">,
  "children" | "onToggle" | "open"
> & {
  summary: ReactNode;
  summaryClassName?: string;
  children: () => ReactNode;
}) {
  const [expanded, setExpanded] = useState(false);
  return (
    <details
      {...props}
      onToggle={(event) => setExpanded(event.currentTarget.open)}
    >
      <summary className={summaryClassName}>{summary}</summary>
      {expanded && children()}
    </details>
  );
}
