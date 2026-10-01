import { useEffect, useRef, useState } from "react";
import { Icon } from "../../Component/Icon";
import { useHealth } from "../../lib/useHealth";
import { showError, usePlaybackStore } from "../../pages/store";
import type { Session } from "../../types/api";
import { refreshWorkspace } from "../library/refreshWorkspace";
import { useLibrary } from "../library/useLibrary";
import type { ChatConversations } from "./useChatConversations";

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
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);
  return (
    <div className="chat-conversation-picker" ref={root}>
      <button
        className="icon-button"
        type="button"
        aria-label="Chat conversations"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        <Icon name="menu" />
      </button>
      {open && (
        <div className="chat-conversation-menu">
          <label htmlFor="chat-session">Lecture session</label>
          <select
            id="chat-session"
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
          <small>Answers use only this lecture session.</small>
          <button
            type="button"
            className="new-conversation"
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
            <p role="status">Loading conversations…</p>
          ) : conversations.list.length ? (
            <ul aria-label="Saved conversations">
              {conversations.list.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
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
            <p>No conversations in this session yet.</p>
          )}
          {health && !health.chatConversations && (
            <small>Restart the API to enable saved conversations.</small>
          )}
        </div>
      )}
    </div>
  );
}
