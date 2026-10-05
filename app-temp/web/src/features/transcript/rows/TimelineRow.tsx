import clsx from "clsx";
import type { ComponentProps, ReactNode, Ref } from "react";

// The layout every timeline row shares: its start time in a narrow column on the left,
// its content on the right. Rows listed together inside a passage are divided by a hairline.
export function TimelineRow({
  time,
  timeTitle,
  mainClassName,
  className,
  children,
  ...props
}: ComponentProps<"article"> & {
  time: string;
  timeTitle?: string;
  mainClassName?: string;
}) {
  return (
    <article
      className={clsx(
        "grid scroll-mt-4 grid-cols-[50px_minmax(0,1fr)] items-start gap-1.75 border-b border-line py-1.75 last:border-b-0 xs:grid-cols-[58px_minmax(0,1fr)] xs:gap-2.5",
        className,
      )}
      {...props}
    >
      <time
        className="pt-1.25 text-[12px] whitespace-nowrap text-muted tabular-nums"
        title={timeTitle}
      >
        {time}
      </time>
      <div className={clsx("min-w-0 py-0.5", mainClassName)}>{children}</div>
    </article>
  );
}

// The muted line over a row: its audio source and time range.
export function RowMeta({ children }: { children: ReactNode }) {
  return (
    <div className="mb-1.5 flex flex-wrap items-center gap-1.5 text-[11px] font-[650] text-muted tabular-nums">
      {children}
    </div>
  );
}

// The row's main line: its text or status, and the play button beside it.
export function RowLine({ children }: { children: ReactNode }) {
  return (
    <div className="grid grid-cols-[minmax(0,1fr)_auto] items-start gap-2.5">
      {children}
    </div>
  );
}

// A row's text or status line, led by a ▸ that turns to ▾ when its details open.
export const rowSummaryStyle =
  "min-w-0 cursor-pointer py-0.5 text-[14px] leading-[1.55] before:mr-1.5 before:text-[10px] before:text-accent before:content-['▸']";

// A row's text that opens to show where it came from: source, model, terms.
// `muted` greys the text of audio that has no transcript yet.
export function RowDetails({
  summary,
  muted = false,
  detailsRef,
  onSelectText,
  children,
}: {
  summary: ReactNode;
  muted?: boolean;
  detailsRef?: Ref<HTMLDetailsElement>;
  onSelectText?: () => void;
  children: ReactNode;
}) {
  return (
    <details className="group/copy min-w-0" ref={detailsRef}>
      <summary
        className={clsx(
          rowSummaryStyle,
          "list-none group-not-open/copy:block group-open/copy:before:content-['▾'] [&::-webkit-details-marker]:hidden",
          muted ? "text-muted" : "text-ink",
        )}
        onMouseUp={onSelectText}
        onKeyUp={onSelectText}
      >
        {summary}
      </summary>
      <div className="py-2 pl-4 text-[11px] text-muted">{children}</div>
    </details>
  );
}

// Sends audio parts whose recognition stopped or came back empty to ASR again.
export function RetryButton({ className, ...props }: ComponentProps<"button">) {
  return (
    <button
      className={clsx(
        "rounded-[7px] border border-[#d7b18d] bg-[#fff9f2] px-2.25 py-1.25 text-[12px] font-bold text-[#885321] disabled:cursor-not-allowed disabled:opacity-55",
        className,
      )}
      {...props}
    />
  );
}
