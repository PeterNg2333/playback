import type { DragEvent } from "react";
import { Icon } from "../Component/Icon";
import type { SessionSummary } from "../types/api";
import type { PlaybackController } from "./handlers";

export function SessionItem({
  item,
  model,
  onDragStart,
  onDragEnd,
}: {
  item: SessionSummary;
  model: PlaybackController;
  onDragStart: (event: DragEvent<HTMLButtonElement>, id: string) => void;
  onDragEnd: () => void;
}) {
  const { session, groups, action, refresh, setNavOpen, moveSession, busy } =
    model;
  return (
    <div className="session-entry">
      <button
        className="session-link"
        aria-current={session?.id === item.id ? "page" : undefined}
        draggable={!busy}
        title="Open session or drag it to a group"
        onDragStart={(event) => onDragStart(event, item.id)}
        onDragEnd={onDragEnd}
        onClick={() => {
          void action("load", () => refresh(item.id));
          setNavOpen(false);
        }}
      >
        <Icon name="document" />
        <span>{item.title}</span>
      </button>
      <details className="nav-menu entry-menu">
        <summary aria-label={`Move ${item.title}`} title={`Move ${item.title}`}>
          <Icon name="more" />
        </summary>
        <div className="nav-menu-popover">
          {item.groupId && (
            <button
              onClick={(event) => {
                event.currentTarget.closest("details")?.removeAttribute("open");
                void moveSession(item.id, null);
              }}
            >
              Move to Sessions
            </button>
          )}
          {groups
            .filter((group) => group.id !== item.groupId)
            .map((group) => (
              <button
                key={group.id}
                onClick={(event) => {
                  event.currentTarget
                    .closest("details")
                    ?.removeAttribute("open");
                  void moveSession(item.id, group.id);
                }}
              >
                Move to {group.name}
              </button>
            ))}
          {!item.groupId && groups.length === 0 && (
            <span className="nav-menu-hint">
              Create a group to move this session.
            </span>
          )}
        </div>
      </details>
    </div>
  );
}
