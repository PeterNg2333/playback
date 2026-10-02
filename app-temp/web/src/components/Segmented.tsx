import clsx from "clsx";
import type { ComponentProps, ReactNode } from "react";

// A row of toggle buttons on a grey track; each pressed one is raised.
export function Segmented({ children }: { children: ReactNode }) {
  return (
    <div className="flex gap-0.5 rounded-lg bg-[#f3f3f9] p-0.75">
      {children}
    </div>
  );
}

export function Segment({
  pressed,
  className,
  ...props
}: ComponentProps<"button"> & { pressed: boolean }) {
  return (
    <button
      aria-pressed={pressed}
      className={clsx(
        "rounded-md px-1.5 py-1.25 text-[11px] font-bold whitespace-nowrap text-muted xs:px-2",
        "aria-pressed:bg-white aria-pressed:text-accent aria-pressed:shadow-[0_1px_4px_#2628481b]",
        className,
      )}
      {...props}
    />
  );
}
