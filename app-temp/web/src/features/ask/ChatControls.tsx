import clsx from "clsx";
import type { ComponentProps } from "react";

// The Ask panel's repeated pieces: its small grey icon buttons and its error box.

export function ChatIconButton({
  className,
  ...props
}: ComponentProps<"button">) {
  return (
    <button
      className={clsx(
        "grid size-6.75 place-items-center rounded-[7px] bg-[#f3f3f9] px-1.5 py-px text-[18px] text-muted",
        className,
      )}
      {...props}
    />
  );
}

// A failed question or a failed source; the question stays in the box for a retry.
export function ChatError(props: ComponentProps<"p">) {
  return (
    <p
      className="mb-2.5 rounded-[7px] border border-[#efb8c9] bg-[#fff2f6] p-2.25 text-[11px] text-[#873859]"
      role="alert"
      {...props}
    />
  );
}
