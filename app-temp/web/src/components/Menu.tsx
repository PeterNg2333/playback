import { useEffect, useRef, type ReactNode, type RefObject } from "react";

// While `open`, calls `close` when the user presses Escape or points outside `element`.
export function useDismiss(
  element: RefObject<HTMLElement | null>,
  open: boolean,
  close: (byEscape: boolean) => void,
) {
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (!element.current?.contains(event.target as Node)) close(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") close(true);
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);
}

// A drop-down on a native <details>: the summary opens it and choosing an item closes it.
// A dismissible menu also closes on a click outside, or on Escape, which returns focus to
// the summary.
export function Menu({
  className,
  label,
  title = label,
  summary,
  dismissible = false,
  children,
}: {
  className: string;
  label: string;
  title?: string;
  summary: ReactNode;
  dismissible?: boolean;
  children: (close: () => void) => ReactNode;
}) {
  const menu = useRef<HTMLDetailsElement>(null);
  const opener = useRef<HTMLElement>(null);
  const close = () => {
    if (menu.current) menu.current.open = false;
  };
  // Listens while mounted and checks the element itself, so an Escape right after opening counts.
  useDismiss(menu, dismissible, (byEscape) => {
    if (!menu.current?.open) return;
    close();
    if (byEscape) opener.current?.focus();
  });
  return (
    <details className={className} ref={menu}>
      <summary ref={opener} aria-label={label} title={title}>
        {summary}
      </summary>
      {children(close)}
    </details>
  );
}
