import type { SessionSummary } from "../types/api";
import type { PlaybackController } from "./handlers";

export function SessionItem({
  item,
  model,
}: {
  item: SessionSummary;
  model: PlaybackController;
}) {
  const { session, groups, action, refresh, setNavOpen, moveSession } = model;
  return (
    <div className="session-entry">
      <button
        className="session-link"
        aria-current={session?.id === item.id ? "page" : undefined}
        onClick={() => {
          action("load", () => refresh(item.id));
          setNavOpen(false);
        }}
      >
        {item.title}
      </button>
      {session?.id === item.id && (
        <label className="session-move">
          Move session
          <select
            aria-label={`Move ${item.title} to group`}
            value={session.groupId || ""}
            onChange={(event) =>
              moveSession(item.id, event.target.value || null)
            }
          >
            <option value="">Ungrouped</option>
            {groups.map((group) => (
              <option key={group.id} value={group.id}>
                {group.name}
              </option>
            ))}
          </select>
        </label>
      )}
    </div>
  );
}
