import { useEffect, useRef, useState } from "react";
import type { DragEvent } from "react";
import { Icon } from "../../Component/Icon";
import { Menu } from "../../Component/Menu";
import { SideNav } from "../../Component/Layout/SideNav";
import type { Group, Session } from "../../lib/backend/schemas";
import { useHealth } from "../../lib/useHealth";
import {
  askForGroupName,
  askForSessionName,
  askToRenameGroup,
  deleteGroup,
  moveSession,
  useLibrary,
} from "./useLibrary";
import { usePlaybackField, usePlaybackStore } from "../../lib/store";
import { SessionItem } from "./SessionItem";
import { AiFlowDialog } from "../activity/AiFlowDialog";

export function LibrarySidebar({ session }: { session: Session | null }) {
  const [navOpen, setNavOpen] = usePlaybackField("navOpen");
  const busy = usePlaybackStore((state) => state.busy);
  const error = usePlaybackStore((state) => state.error);
  const health = useHealth();
  const { sessions, groups } = useLibrary();
  const disabled = !health?.mongo || !!busy;
  const [flowGroup, setFlowGroup] = useState<Group | null>(null);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [draggedId, setDraggedId] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<string | null>(null);
  const [deleteCandidate, setDeleteCandidate] = useState<Group | null>(null);
  const deleteDialog = useRef<HTMLDialogElement>(null);
  const ungrouped = sessions.filter(
    (item) =>
      !item.groupId || !groups.some((group) => group.id === item.groupId),
  );

  useEffect(() => {
    if (deleteCandidate && !deleteDialog.current?.open)
      deleteDialog.current?.showModal();
    if (!deleteCandidate && deleteDialog.current?.open)
      deleteDialog.current.close();
  }, [deleteCandidate]);

  function dragStart(event: DragEvent<HTMLButtonElement>, id: string) {
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("application/x-playback-session", id);
    setDraggedId(id);
  }

  function dragOver(event: DragEvent<HTMLElement>, groupId: string | null) {
    if (!draggedId || disabled) return;
    const item = sessions.find((session) => session.id === draggedId);
    if (!item || (item.groupId || null) === groupId) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    setDropTarget(groupId || "sessions");
  }

  function dragLeave(event: DragEvent<HTMLElement>) {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null))
      setDropTarget(null);
  }

  function drop(event: DragEvent<HTMLElement>, groupId: string | null) {
    event.preventDefault();
    const id = event.dataTransfer.getData("application/x-playback-session");
    const item = sessions.find((session) => session.id === id);
    if (item && !disabled && (item.groupId || null) !== groupId)
      void moveSession(id, groupId);
    setDraggedId(null);
    setDropTarget(null);
  }

  const sessionProps = {
    currentId: session?.id,
    groups,
    onDragStart: dragStart,
    onDragEnd: () => {
      setDraggedId(null);
      setDropTarget(null);
    },
  };

  return (
    <SideNav open={navOpen} label="Sessions and groups">
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
      <section className="sidebar-section" aria-labelledby="groups-heading">
        <div className="sidebar-heading">
          <h2 id="groups-heading">Groups</h2>
          <button
            className="nav-icon-button sidebar-create"
            aria-label="New group"
            title="New group"
            onClick={askForGroupName}
            disabled={disabled}
          >
            <Icon name="plus" />
          </button>
        </div>
        {groups.map((group) => {
          const open = expanded[group.id] !== false;
          const members = sessions.filter((item) => item.groupId === group.id);
          return (
            <div
              className="session-group"
              data-drop-target={dropTarget === group.id || undefined}
              key={group.id}
              onDragOver={(event) => dragOver(event, group.id)}
              onDragLeave={dragLeave}
              onDrop={(event) => drop(event, group.id)}
            >
              <div className="folder-row">
                <button
                  className="folder-toggle"
                  aria-expanded={open}
                  aria-label={`${open ? "Collapse" : "Expand"} ${group.name}`}
                  onClick={() =>
                    setExpanded((current) => ({
                      ...current,
                      [group.id]: !open,
                    }))
                  }
                >
                  <Icon name="folder" />
                  <span>{group.name}</span>
                  <Icon name={open ? "chevron-down" : "chevron-right"} />
                </button>
                <button
                  className="folder-create nav-icon-button"
                  aria-label={`New session in ${group.name}`}
                  title={`New session in ${group.name}`}
                  onClick={() => askForSessionName(group.id)}
                  disabled={disabled}
                >
                  <Icon name="plus" />
                </button>
                <Menu
                  className="nav-menu group-menu"
                  label={`Group options for ${group.name}`}
                  summary={<Icon name="more" />}
                >
                  {(close) => (
                    <div className="nav-menu-popover">
                      <button
                        onClick={() => {
                          close();
                          setFlowGroup(group);
                        }}
                      >
                        View AI flow
                      </button>
                      <button
                        onClick={() => {
                          close();
                          askToRenameGroup(group.id, group.name);
                        }}
                      >
                        Rename group
                      </button>
                      <button
                        className="danger-action"
                        onClick={() => {
                          close();
                          setDeleteCandidate(group);
                        }}
                      >
                        Delete group
                      </button>
                    </div>
                  )}
                </Menu>
              </div>
              {open && (
                <div className="group-children">
                  {members.map((item) => (
                    <SessionItem key={item.id} item={item} {...sessionProps} />
                  ))}
                </div>
              )}
            </div>
          );
        })}
        {groups.length === 0 && (
          <p className="nav-empty">Create a group to organise sessions.</p>
        )}
      </section>

      <section
        className="sidebar-section sessions-section"
        aria-labelledby="sessions-heading"
        data-drop-target={dropTarget === "sessions" || undefined}
        onDragOver={(event) => dragOver(event, null)}
        onDragLeave={dragLeave}
        onDrop={(event) => drop(event, null)}
      >
        <div className="sidebar-heading">
          <h2 id="sessions-heading">
            <Icon name="folder" /> Sessions
          </h2>
          <button
            className="nav-icon-button sidebar-create"
            aria-label="New session"
            title="New session"
            onClick={() => askForSessionName()}
            disabled={disabled}
          >
            <Icon name="plus" />
          </button>
        </div>
        {ungrouped.map((item) => (
          <SessionItem key={item.id} item={item} {...sessionProps} />
        ))}
        {ungrouped.length === 0 && (
          <p className="nav-empty">Sessions without a group appear here.</p>
        )}
      </section>

      {flowGroup && (
        <AiFlowDialog
          key={flowGroup.id}
          group={flowGroup}
          onClose={() => setFlowGroup(null)}
        />
      )}
      <dialog
        ref={deleteDialog}
        className="text-dialog delete-group-dialog"
        aria-labelledby="delete-group-title"
        onCancel={(event) => {
          event.preventDefault();
          if (!busy) setDeleteCandidate(null);
        }}
      >
        <div className="delete-group-content">
          <h2 id="delete-group-title">Delete {deleteCandidate?.name}?</h2>
          <p>
            Sessions in this group will move to Sessions. Their notes and
            transcripts will stay intact.
          </p>
          {error && <p role="alert">{error}</p>}
          <div className="text-dialog-actions">
            <button onClick={() => setDeleteCandidate(null)} disabled={!!busy}>
              Cancel
            </button>
            <button
              className="danger-action"
              onClick={async () => {
                if (deleteCandidate && (await deleteGroup(deleteCandidate.id)))
                  setDeleteCandidate(null);
              }}
              disabled={!!busy}
            >
              Delete group
            </button>
          </div>
        </div>
      </dialog>
    </SideNav>
  );
}
