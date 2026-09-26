import type { ReactNode } from "react";

export function Panel({
  className,
  children,
}: {
  className: string;
  children: ReactNode;
}) {
  return <section className={`panel ${className}`}>{children}</section>;
}
