import clsx from "clsx";
import type { ComponentProps, ReactNode } from "react";

// One of the page's two columns: a bordered card whose body scrolls on its own.
// Below xl the columns stack into tabs and fill the height.
export function Panel({ className, ...props }: ComponentProps<"section">) {
  return (
    <section
      className={clsx(
        "flex h-full min-h-0 min-w-0 flex-col overflow-hidden rounded-xl border border-line bg-surface max-xl:flex-1",
        className,
      )}
      {...props}
    />
  );
}

// The bar across the top of a panel: its title on the left, its controls on the right.
export function PanelHeader({
  title,
  actions,
  wrap = false,
  className,
}: {
  title: ReactNode;
  actions: ReactNode;
  wrap?: boolean;
  className?: string;
}) {
  return (
    <div
      className={clsx(
        "flex min-h-14 flex-none items-center justify-between border-b border-line px-2.5 xs:px-3 lg:px-4.5",
        wrap ? "flex-wrap gap-2.5" : "gap-3",
        className,
      )}
    >
      {title}
      <div className="flex items-center gap-0.75 xs:gap-2">{actions}</div>
    </div>
  );
}
