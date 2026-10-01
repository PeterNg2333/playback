import type { ReactNode } from "react";

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
    <nav className={`sidebar ${open ? "is-open" : ""}`} aria-label={label}>
      {children}
    </nav>
  );
}
