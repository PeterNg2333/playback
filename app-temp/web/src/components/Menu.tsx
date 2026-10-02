import clsx from "clsx";
import {
  useEffect,
  useRef,
  type ComponentProps,
  type ReactNode,
  type RefObject,
} from "react";

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
  summaryClassName,
  label,
  title = label,
  summary,
  dismissible = false,
  children,
}: {
  className: string;
  summaryClassName?: string;
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
      <summary
        ref={opener}
        className={clsx(
          "cursor-pointer list-none [&::-webkit-details-marker]:hidden",
          summaryClassName,
        )}
        aria-label={label}
        title={title}
      >
        {summary}
      </summary>
      {children(close)}
    </details>
  );
}

// The panel of an action menu: a short scrolling list under the summary, right-aligned.
export function MenuList({ children }: { children: ReactNode }) {
  return (
    <div className="absolute top-7 right-0 z-20 max-h-57.5 w-max max-w-56.25 min-w-38.75 overflow-y-auto rounded-lg border border-line bg-white p-1 shadow-[0_8px_24px_#24263b20]">
      {children}
    </div>
  );
}

// One action in a MenuList; `danger` marks one that deletes something.
export function MenuItem({
  danger = false,
  ...props
}: ComponentProps<"button"> & { danger?: boolean }) {
  return (
    <button
      className={clsx(
        "block w-full rounded-[5px] px-2.25 py-1.75 text-left text-[11px] hover:bg-[#f0f1f6]",
        danger ? "text-[#a43f47]" : "text-ink",
      )}
      {...props}
    />
  );
}
