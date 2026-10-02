import type { ReactNode } from "react";

// The page's top bar. Phones and tablets lay it out as a three-column grid
// (menu and brand, title, recorder); xl screens as a row with the title centred over it.
export function Header({ children }: { children: ReactNode }) {
  return (
    <header className="relative z-12 grid h-14 flex-none grid-cols-[auto_minmax(0,1fr)_auto] items-center justify-between gap-2 border-b border-line bg-white px-3 md:h-16 md:gap-2.5 md:px-5.5 xl:flex xl:gap-4">
      {children}
    </header>
  );
}
