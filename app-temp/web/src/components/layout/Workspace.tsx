import type { ReactNode } from "react";

// The area right of the sidebar: the notes and transcript panels side by side on xl
// screens, stacked as tabs below that.
export function Workspace({
  id = "workspace",
  children,
}: {
  id?: string;
  children: ReactNode;
}) {
  return (
    <main
      className="flex min-h-0 flex-1 flex-col gap-4 self-stretch overflow-hidden px-2.5 pt-2.5 pb-3 md:ml-54 md:p-4 lg:ml-62 xl:grid xl:grid-cols-2"
      id={id}
    >
      {children}
    </main>
  );
}
