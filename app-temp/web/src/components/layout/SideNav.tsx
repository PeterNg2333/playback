import clsx from "clsx";
import type { ReactNode } from "react";

// The left sidebar. From md it is always shown beside the workspace; on phones it slides
// in over the page while `open` and is hidden from pointer and screen reader otherwise.
export function SideNav({
  open,
  label,
  children,
}: {
  open: boolean;
  label: string;
  children: ReactNode;
}) {
  return (
    <nav
      className={clsx(
        "fixed top-14 bottom-0 left-0 z-11 w-[min(280px,84vw)] overflow-y-auto border-r border-line bg-[#f7f8fb] px-3 py-4.5 shadow-[8px_0_24px_#24263b12] transition-transform duration-200 ease-[ease] motion-reduce:transition-none",
        "md:pointer-events-auto md:visible md:top-16 md:w-54 md:translate-x-0 md:shadow-none md:transition-none lg:w-62",
        open
          ? "translate-x-0"
          : "pointer-events-none invisible -translate-x-full",
      )}
      aria-label={label}
    >
      {children}
    </nav>
  );
}
