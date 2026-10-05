import clsx from "clsx";
import type { ComponentProps } from "react";

// A small tinted button naming a source (a lecture time, a material) that opens it.
// On an already tinted background, `onTint` makes it white.
export function Chip({
  onTint = false,
  className,
  ...props
}: ComponentProps<"button"> & { onTint?: boolean }) {
  return (
    <button
      className={clsx(
        "mr-1.25 rounded-[5px] px-1.5 py-0.75 text-[10px] font-[750] text-accent",
        onTint ? "bg-white" : "bg-accent-soft",
        className,
      )}
      {...props}
    />
  );
}
