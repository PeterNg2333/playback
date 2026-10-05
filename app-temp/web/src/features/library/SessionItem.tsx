import clsx from "clsx";
import type { DragEvent } from "react";
import { Icon } from "../../components/Icon";
import { MenuItem } from "../../components/Menu";
import type { Group, SessionSummary } from "../../lib/backend/schemas";
import { moveSession, openSession } from "./useLibrary";
import { usePlaybackStore } from "../../lib/store";
import { RowMenu, SidebarRow } from "./SidebarControls";

export function SessionItem({
  item,
  currentId,
  groups,
  onDragStart,
  onDragEnd,
}: {
  item: SessionSummary;
  currentId: string | undefined;
  groups: Group[];
  onDragStart: (event: DragEvent<HTMLButtonElement>, id: string) => void;
  onDragEnd: () => void;
}) {
  const busy = usePlaybackStore((state) => state.busy);
  return (
    <SidebarRow>
      <button
        className={clsx(
          "flex min-w-0 flex-1 items-center gap-2.25 rounded-lg px-2.5 py-2 text-left text-[12px] text-ink aria-[current=page]:bg-[#e3e6ef] aria-[current=page]:font-bold",
          !busy && "cursor-grab active:cursor-grabbing",
        )}
        aria-current={currentId === item.id ? "page" : undefined}
        draggable={!busy}
        title="Open session or drag it to a group"
        onDragStart={(event) => onDragStart(event, item.id)}
        onDragEnd={onDragEnd}
        onClick={() => {
          void openSession(item.id);
          usePlaybackStore.setState({ navOpen: false });
        }}
      >
        <Icon name="document" className="size-3.75 flex-none text-[#858fa1]" />
        <span className="min-w-0 truncate">{item.title}</span>
      </button>
      <RowMenu label={`Move ${item.title}`}>
        {(close) => (
          <>
            {item.groupId && (
              <MenuItem
                onClick={() => {
                  close();
                  void moveSession(item.id, null);
                }}
              >
                Move to Sessions
              </MenuItem>
            )}
            {groups
              .filter((group) => group.id !== item.groupId)
              .map((group) => (
                <MenuItem
                  key={group.id}
                  onClick={() => {
                    close();
                    void moveSession(item.id, group.id);
                  }}
                >
                  Move to {group.name}
                </MenuItem>
              ))}
            {!item.groupId && groups.length === 0 && (
              <span className="block p-2 text-[11px] text-muted">
                Create a group to move this session.
              </span>
            )}
          </>
        )}
      </RowMenu>
    </SidebarRow>
  );
}
