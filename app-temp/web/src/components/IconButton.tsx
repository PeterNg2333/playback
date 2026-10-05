import clsx from "clsx";
import type { ComponentProps } from "react";

// A square bordered button holding an icon or one symbol: 34px, or 24px when `small`.
export function IconButton({
  small = false,
  className,
  ...props
}: ComponentProps<"button"> & { small?: boolean }) {
  return (
    <button
      className={clsx(
        "inline-grid place-items-center rounded-[9px] border border-line bg-white text-ink [&_svg]:size-4.25",
        small ? "size-6" : "size-8.5",
        className,
      )}
      {...props}
    />
  );
}
