import { SideNav } from "../Component/Layout/SideNav";
import type { PlaybackController } from "./handlers";
import { SessionItem } from "./SessionItem";

export function SessionNav({ model }: { model: PlaybackController }) {
  const {
    navOpen,
    setNavOpen,
    health,
    busy,
    groups,
    sessions,
    create,
    createGroup,
    renameGroup,
  } = model;
  const disabled = !health?.mongo || !!busy;

  return (
    <SideNav open={navOpen} label="Sessions">
      <div className="sidebar-top">
        <span>Workspace</span>
        <button
          className="nav-close icon-control"
          aria-label="Close sessions"
          onClick={() => setNavOpen(false)}
        >
          ×
        </button>
      </div>
      <button
        className="new-session"
        aria-label="New session"
        onClick={() => create()}
        disabled={disabled}
      >
        + New session
      </button>
      <div className="sidebar-heading">
        <span>Groups</span>
        <button
          aria-label="New group"
          onClick={createGroup}
          disabled={disabled}
        >
          +
        </button>
      </div>
      {groups.map((group) => (
        <div className="session-group" key={group.id}>
          <div className="group-heading">
            <span>{group.name}</span>
            <button
              aria-label={`Rename ${group.name}`}
              onClick={() => renameGroup(group.id, group.name)}
            >
              •••
            </button>
          </div>
          <button
            className="group-create"
            aria-label={`New session in ${group.name}`}
            onClick={() => create(group.id)}
            disabled={disabled}
          >
            + New session
          </button>
          {sessions
            .filter((item) => item.groupId === group.id)
            .map((item) => (
              <SessionItem key={item.id} item={item} model={model} />
            ))}
        </div>
      ))}
      <div className="sidebar-heading recent-heading">Ungrouped sessions</div>
      {sessions
        .filter(
          (item) =>
            !item.groupId || !groups.some((group) => group.id === item.groupId),
        )
        .map((item) => (
          <SessionItem key={item.id} item={item} model={model} />
        ))}
    </SideNav>
  );
}
