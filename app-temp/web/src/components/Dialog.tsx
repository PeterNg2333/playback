import clsx from "clsx";
import type { ComponentProps } from "react";

// A modal panel for one short task, such as naming a group, centred over a dimmed page.
export function Dialog({ className, ...props }: ComponentProps<"dialog">) {
  return (
    <dialog
      className={clsx(
        "m-auto w-[min(420px,calc(100vw-32px))] max-w-none rounded-xl border border-line bg-surface p-0 text-ink shadow-[0_20px_60px_#25243c35] backdrop:bg-[#25243c80]",
        className,
      )}
      {...props}
    />
  );
}

// The dialog's buttons, right-aligned under its content.
export function DialogActions({ children }: ComponentProps<"div">) {
  return <div className="mt-2 flex justify-end gap-2">{children}</div>;
}

// Plain for cancelling, primary for the dialog's action, danger when it deletes something.
// While the dialog saves, its buttons show a waiting cursor.
export function DialogButton({
  variant = "plain",
  className,
  ...props
}: ComponentProps<"button"> & { variant?: "plain" | "primary" | "danger" }) {
  return (
    <button
      className={clsx(
        "rounded-lg border px-3.5 py-2 font-bold disabled:cursor-wait disabled:opacity-55",
        variant === "primary" && "border-accent bg-accent text-white",
        variant === "danger" && "border-[#a43f47] bg-[#a43f47] text-white",
        variant === "plain" && "border-line bg-white text-ink",
        className,
      )}
      {...props}
    />
  );
}
