import clsx from "clsx";
import { useEffect, useRef, useState } from "react";
import type { DragEvent } from "react";
import { Icon } from "../../components/Icon";
import { IconButton } from "../../components/IconButton";
import { MenuItem } from "../../components/Menu";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { SideNav } from "../../components/layout/SideNav";
import { RowMenu, SidebarButton, SidebarRow } from "./SidebarControls";
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
      <div className="mx-2.5 mt-px mb-3.5 flex items-center justify-between text-[10px] font-bold tracking-[0.09em] text-muted uppercase">
        <span>Workspace</span>
        <IconButton
          small
          className="tracking-[normal] md:hidden"
          aria-label="Close sessions"
          onClick={() => setNavOpen(false)}
        >
          ×
        </IconButton>
      </div>
      <section className={sectionStyle} aria-labelledby="groups-heading">
        <div className={headingStyle}>
          <h2 id="groups-heading" className={headingTextStyle}>
            Groups
          </h2>
          <SidebarButton
            create
            aria-label="New group"
            title="New group"
            onClick={askForGroupName}
            disabled={disabled}
          >
            <Icon name="plus" className="size-3.75" />
          </SidebarButton>
        </div>
        {groups.map((group) => {
          const open = expanded[group.id] !== false;
          const members = sessions.filter((item) => item.groupId === group.id);
          return (
            <div
              className={dropTargetStyle}
              data-testid="session-group"
              data-drop-target={dropTarget === group.id || undefined}
              key={group.id}
              onDragOver={(event) => dragOver(event, group.id)}
              onDragLeave={dragLeave}
              onDrop={(event) => drop(event, group.id)}
            >
              <SidebarRow>
                <button
                  className="flex min-w-0 flex-1 items-center gap-2.25 px-2.25 py-2 text-left text-[12px] font-[620] text-ink"
                  aria-expanded={open}
                  aria-label={`${open ? "Collapse" : "Expand"} ${group.name}`}
                  onClick={() =>
                    setExpanded((current) => ({
                      ...current,
                      [group.id]: !open,
                    }))
                  }
                >
                  <Icon
                    name="folder"
                    className="size-4 flex-none text-[#5e697b]"
                  />
                  <span className="min-w-0 truncate">{group.name}</span>
                  <Icon
                    name={open ? "chevron-down" : "chevron-right"}
                    className="ml-auto size-3.25 flex-none text-[#8b94a3]"
                  />
                </button>
                <SidebarButton
                  reveal
                  aria-label={`New session in ${group.name}`}
                  title={`New session in ${group.name}`}
                  onClick={() => askForSessionName(group.id)}
                  disabled={disabled}
                >
                  <Icon name="plus" className="size-3.75" />
                </SidebarButton>
                <RowMenu label={`Group options for ${group.name}`}>
                  {(close) => (
                    <>
                      <MenuItem
                        onClick={() => {
                          close();
                          setFlowGroup(group);
                        }}
                      >
                        View AI flow
                      </MenuItem>
                      <MenuItem
                        onClick={() => {
                          close();
                          askToRenameGroup(group.id, group.name);
                        }}
                      >
                        Rename group
                      </MenuItem>
                      <MenuItem
                        danger
                        onClick={() => {
                          close();
                          setDeleteCandidate(group);
                        }}
                      >
                        Delete group
                      </MenuItem>
                    </>
                  )}
                </RowMenu>
              </SidebarRow>
              {open && (
                <div className="mt-0.5 mb-1.25 ml-3.25 border-l border-[#dfe3ec] pl-1.75">
                  {members.map((item) => (
                    <SessionItem key={item.id} item={item} {...sessionProps} />
                  ))}
                </div>
              )}
            </div>
          );
        })}
        {groups.length === 0 && (
          <p className={emptyStyle}>Create a group to organise sessions.</p>
        )}
      </section>

      <section
        className={clsx(sectionStyle, dropTargetStyle, "pb-2.5")}
        aria-labelledby="sessions-heading"
        data-drop-target={dropTarget === "sessions" || undefined}
        onDragOver={(event) => dragOver(event, null)}
        onDragLeave={dragLeave}
        onDrop={(event) => drop(event, null)}
      >
        <div className={headingStyle}>
          <h2
            id="sessions-heading"
            className={clsx(headingTextStyle, "flex items-center gap-1.75")}
          >
            <Icon name="folder" className="size-3.75" /> Sessions
          </h2>
          <SidebarButton
            create
            aria-label="New session"
            title="New session"
            onClick={() => askForSessionName()}
            disabled={disabled}
          >
            <Icon name="plus" className="size-3.75" />
          </SidebarButton>
        </div>
        {ungrouped.map((item) => (
          <SessionItem key={item.id} item={item} {...sessionProps} />
        ))}
        {ungrouped.length === 0 && (
          <p className={emptyStyle}>Sessions without a group appear here.</p>
        )}
      </section>

      {flowGroup && (
        <AiFlowDialog
          key={flowGroup.id}
          group={flowGroup}
          onClose={() => setFlowGroup(null)}
        />
      )}
      <Dialog
        ref={deleteDialog}
        aria-labelledby="delete-group-title"
        onCancel={(event) => {
          event.preventDefault();
          if (!busy) setDeleteCandidate(null);
        }}
      >
        <div className="p-5.5">
          <h2 id="delete-group-title" className="mb-1 text-[17px] font-[750]">
            Delete {deleteCandidate?.name}?
          </h2>
          <p className="mt-2 mb-4.5 text-[12px] text-muted">
            Sessions in this group will move to Sessions. Their notes and
            transcripts will stay intact.
          </p>
          {error && (
            <p className="text-[12px] text-[#a13232]" role="alert">
              {error}
            </p>
          )}
          <DialogActions>
            <DialogButton
              onClick={() => setDeleteCandidate(null)}
              disabled={!!busy}
            >
              Cancel
            </DialogButton>
            <DialogButton
              variant="danger"
              onClick={async () => {
                if (deleteCandidate && (await deleteGroup(deleteCandidate.id)))
                  setDeleteCandidate(null);
              }}
              disabled={!!busy}
            >
              Delete group
            </DialogButton>
          </DialogActions>
        </div>
      </Dialog>
    </SideNav>
  );
}

// The Groups and Sessions sections share their spacing and heading.
const sectionStyle = "mt-6.25 min-h-9.5 rounded-[9px]";
const headingStyle =
  "mx-1.75 mb-1.75 flex min-h-7 items-center justify-between px-1 text-muted";
const headingTextStyle = "text-[11px] font-bold tracking-[0.02em]";
const emptyStyle = "mx-2.5 my-1 text-[11px] leading-[1.4] text-muted";
// A group or the Sessions list, highlighted while a dragged session is over it.
const dropTargetStyle =
  "data-drop-target:bg-[#e9e8f8] data-drop-target:outline-2 data-drop-target:-outline-offset-2 data-drop-target:outline-accent";
