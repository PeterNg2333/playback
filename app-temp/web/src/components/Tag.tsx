import clsx from "clsx";
import type { ComponentProps } from "react";

// A rounded outlined button for a short label, such as a key term to ask about.
export function Tag({ className, ...props }: ComponentProps<"button">) {
  return (
    <button
      className={clsx(
        "mt-2 mr-1.25 inline-block rounded-full border border-line bg-[#f7f8fb] px-1.75 py-0.75 text-[10px] text-ink hover:border-accent",
        className,
      )}
      {...props}
    />
  );
}
