import clsx from "clsx";
import type { ComponentProps } from "react";

// The small tinted chip a note shows inline for a cited source or a saved explanation.
const chipStyle =
  "mx-0.5 rounded-sm border border-line px-1.25 py-px text-[11px]";
const tintStyle = "cursor-pointer bg-accent-soft text-accent";

export function CitationChip({
  className,
  ...props
}: ComponentProps<"button">) {
  return (
    <button
      type="button"
      className={clsx(chipStyle, tintStyle, className)}
      {...props}
    />
  );
}

// The same chip when it cannot be opened. `unavailable` marks a citation that matches no
// source in the session.
export function CitationLabel({
  unavailable = false,
  className,
  ...props
}: ComponentProps<"span"> & { unavailable?: boolean }) {
  return (
    <span
      className={clsx(
        chipStyle,
        unavailable ? "cursor-help bg-[#fff8ed] text-[#9a591c]" : tintStyle,
        className,
      )}
      {...props}
    />
  );
}
