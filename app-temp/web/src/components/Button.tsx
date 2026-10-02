import clsx from "clsx";
import type { ComponentProps } from "react";

// A small text button: white with a hairline border, or filled with the accent for the
// panel's main action.
export function Button({
  variant = "secondary",
  className,
  ...props
}: ComponentProps<"button"> & { variant?: "primary" | "secondary" }) {
  return (
    <button
      className={clsx(
        "rounded-[7px] border px-2.75 py-1.75 text-[11px] font-bold",
        variant === "primary"
          ? "border-accent bg-accent text-white"
          : "border-line bg-white text-ink",
        className,
      )}
      {...props}
    />
  );
}
