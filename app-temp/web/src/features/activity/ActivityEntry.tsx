import clsx from "clsx";
import type { ReactNode } from "react";
import { LazyDetails } from "../../components/LazyDetails";

// One recorded event in the AI activity popover (a model call, a note version, a term
// decision): a bordered card whose summary shows a title, a muted detail line under it
// and a short aside on the right. The body is built only while the card is open.
export function ActivityEntry({
  title,
  detail,
  detailClassName,
  aside,
  asideClassName,
  children,
}: {
  title: ReactNode;
  detail: ReactNode;
  detailClassName?: string;
  aside?: ReactNode;
  asideClassName?: string;
  children: () => ReactNode;
}) {
  return (
    <LazyDetails
      className="mt-2 rounded-lg border border-line"
      data-testid="activity-entry"
      summaryClassName="grid cursor-pointer list-none grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-1.25 px-3 py-2.5 [&::-webkit-details-marker]:hidden"
      summary={
        <>
          <strong className="text-[12px] wrap-anywhere">{title}</strong>
          <span
            className={clsx(
              "col-start-1 row-start-2 text-[10px] font-semibold",
              detailClassName ?? "text-muted",
            )}
          >
            {detail}
          </span>
          {aside && (
            <small
              className={clsx(
                "col-start-2 row-[1/3] self-center text-[10px]",
                asideClassName ?? "text-muted",
              )}
            >
              {aside}
            </small>
          )}
        </>
      }
    >
      {children}
    </LazyDetails>
  );
}

// A muted explanation under an activity heading or inside an entry.
export function ActivityHelp({ children }: { children: ReactNode }) {
  return (
    <p className="mb-2.75 text-[11px] leading-[1.6] text-muted">{children}</p>
  );
}
