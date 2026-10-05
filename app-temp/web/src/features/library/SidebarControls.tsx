import clsx from "clsx";
import type { ComponentProps, ReactNode } from "react";
import { Icon } from "../../components/Icon";
import { Menu, MenuList } from "../../components/Menu";

// The sidebar's building blocks: a group or session row, the actions it reveals on hover,
// and the small square icon buttons.

// Row actions show while the row is hovered or focused, and always on touch screens.
const revealOnRowHover =
  "opacity-0 group-hover/row:opacity-100 group-focus-within/row:opacity-100 hover-none:opacity-100";

export function SidebarRow({ children }: { children: ReactNode }) {
  return (
    <div className="group/row relative flex min-w-0 items-center rounded-lg hover:bg-[#eceef4]">
      {children}
    </div>
  );
}

// The "…" menu at the end of a row; it stays visible while open.
export function RowMenu({
  label,
  children,
}: {
  label: string;
  children: (close: () => void) => ReactNode;
}) {
  return (
    <Menu
      className={clsx(
        "relative flex-none open:z-21 open:opacity-100",
        revealOnRowHover,
      )}
      summaryClassName="grid h-6.5 w-6 place-items-center rounded-md text-muted hover:bg-[#e8eaf0] hover:text-ink"
      label={label}
      summary={<Icon name="more" className="size-3.75" />}
    >
      {(close) => <MenuList>{children(close)}</MenuList>}
    </Menu>
  );
}

// A 26px icon button. `create` fills the + beside a heading with the accent; `reveal`
// hides it until its row is hovered.
export function SidebarButton({
  create = false,
  reveal = false,
  className,
  ...props
}: ComponentProps<"button"> & { create?: boolean; reveal?: boolean }) {
  return (
    <button
      className={clsx(
        "grid size-6.5 flex-none place-items-center rounded-md disabled:cursor-not-allowed disabled:opacity-50",
        create
          ? "bg-accent text-white hover:bg-[color-mix(in_srgb,var(--color-accent)_84%,#202040)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          : "text-muted hover:bg-[#e8eaf0] hover:text-ink",
        reveal && revealOnRowHover,
        className,
      )}
      {...props}
    />
  );
}
