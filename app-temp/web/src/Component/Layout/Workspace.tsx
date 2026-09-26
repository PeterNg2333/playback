import type { ReactNode } from "react";

export function Workspace({
  id = "workspace",
  children,
}: {
  id?: string;
  children: ReactNode;
}) {
  return (
    <main className="workspace" id={id}>
      {children}
    </main>
  );
}
