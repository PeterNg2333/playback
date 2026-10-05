import clsx from "clsx";
import { useRef, useState } from "react";
import { Icon } from "../../components/Icon";
import { useDismiss } from "../../components/Menu";
import { useHealth } from "../../lib/useHealth";
import { showError, usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { useLibrary } from "../library/useLibrary";
import type { ChatConversations } from "./useChatConversations";
import { ChatIconButton } from "./ChatControls";

export function ChatConversationMenu({
  session,
  conversations,
  onNewConversation,
  onSelectConversation,
}: {
  session: Session | null;
  conversations: ChatConversations;
  onNewConversation: () => Promise<void>;
  onSelectConversation: (id: string) => Promise<void>;
}) {
  const { sessions, groups } = useLibrary();
  const health = useHealth();
  const busy = usePlaybackStore((state) => state.busy);
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  useDismiss(root, open, () => setOpen(false));
  return (
    <div ref={root}>
      <ChatIconButton
        type="button"
        aria-label="Chat conversations"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        <Icon name="menu" className="size-4" />
      </ChatIconButton>
      {open && (
        // Drops down over the conversation, below the Ask panel's header.
        <div className="absolute top-16 right-3 left-3 z-2 max-h-[min(350px,45dvh)] overflow-y-auto rounded-[10px] border border-line bg-surface p-3.5 shadow-[0_12px_35px_#25243c25]">
          <label
            htmlFor="chat-session"
            className="mb-1.5 block text-[11px] font-bold"
          >
            Lecture session
          </label>
          {/* The browser's own drop-down font and text colour. */}
          <select
            id="chat-session"
            className="w-full rounded-md border border-line bg-surface p-2 [font:revert] text-black"
            value={session?.id ?? ""}
            disabled={!!busy || conversations.loading}
            onChange={(event) => {
              void refreshWorkspace(event.target.value).catch(showError);
            }}
          >
            {!session && <option value="">Select a lecture session</option>}
            {groups.map((group) => (
              <optgroup key={group.id} label={group.name}>
                {sessions
                  .filter((item) => item.groupId === group.id)
                  .map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.title}
                    </option>
                  ))}
              </optgroup>
            ))}
            <optgroup label="Ungrouped sessions">
              {sessions
                .filter((item) => !item.groupId)
                .map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.title}
                  </option>
                ))}
            </optgroup>
          </select>
          <small className={noteStyle}>
            Answers use only this lecture session.
          </small>
          <button
            type="button"
            className={clsx(itemStyle, "bg-accent-soft text-accent")}
            disabled={
              !session ||
              !health?.chatConversations ||
              !!busy ||
              conversations.loading
            }
            onClick={async () => {
              await onNewConversation();
              setOpen(false);
            }}
          >
            ＋ New conversation
          </button>
          {conversations.loading ? (
            <p className={messageStyle} role="status">
              Loading conversations…
            </p>
          ) : conversations.list.length ? (
            <ul className="mt-2" aria-label="Saved conversations">
              {conversations.list.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
                    className={clsx(
                      itemStyle,
                      "text-ink aria-pressed:bg-accent-soft aria-pressed:text-accent",
                    )}
                    disabled={!!busy}
                    aria-pressed={conversations.current?.id === item.id}
                    onClick={async () => {
                      await onSelectConversation(item.id);
                      setOpen(false);
                    }}
                  >
                    {item.title}
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p className={messageStyle}>
              No conversations in this session yet.
            </p>
          )}
          {health && !health.chatConversations && (
            <small className={noteStyle}>
              Restart the API to enable saved conversations.
            </small>
          )}
        </div>
      )}
    </div>
  );
}

const noteStyle = "my-1.5 block text-[10px] text-muted";
const messageStyle = "mb-2.75 text-[11px] text-muted";
const itemStyle =
  "w-full cursor-pointer rounded-md p-2.25 text-left text-[12px]";
