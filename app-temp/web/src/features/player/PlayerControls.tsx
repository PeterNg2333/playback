import clsx from "clsx";
import type { ComponentProps } from "react";

// The audio player's bordered buttons and drop-downs, dimmed while nothing can play.
const control =
  "cursor-pointer disabled:cursor-not-allowed disabled:opacity-48";

// `primary` is the round play/pause button in the middle of the bar.
export function PlayerButton({
  primary = false,
  className,
  ...props
}: ComponentProps<"button"> & { primary?: boolean }) {
  return (
    <button
      className={clsx(
        control,
        primary
          ? "rounded-full bg-accent text-white"
          : "rounded-[7px] border border-line bg-white text-ink",
        className,
      )}
      {...props}
    />
  );
}

export function PlayerSelect({
  className,
  ...props
}: ComponentProps<"select">) {
  return (
    <select
      className={clsx(
        control,
        "rounded-[7px] border border-line bg-white px-0.5 py-1 text-[10px] text-ink md:px-1.5 md:text-[11px]",
        className,
      )}
      {...props}
    />
  );
}
