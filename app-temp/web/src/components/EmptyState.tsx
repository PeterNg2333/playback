import type { ReactNode } from "react";

// A muted line saying there is nothing to show here yet.
export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="my-6 text-[12px] text-muted">{children}</p>;
}
