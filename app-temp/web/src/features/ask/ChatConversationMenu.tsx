import clsx from "clsx";
import { useEffect, useRef, useState } from "react";
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
  const [search, setSearch] = useState("");
  const root = useRef<HTMLDivElement>(null);
  function close() {
    setOpen(false);
    root.current?.querySelector<HTMLButtonElement>("button")?.focus();
  }
  useDismiss(root, open, (byEscape) => {
    setOpen(false);
    if (byEscape)
      root.current?.querySelector<HTMLButtonElement>("button")?.focus();
  });
  useEffect(() => setSearch(""), [session?.id]);
  const recent = [...conversations.list]
    .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
    .filter((item) =>
      item.title
        .toLocaleLowerCase()
        .includes(search.trim().toLocaleLowerCase()),
    );
  return (
    <div ref={root}>
      <ChatIconButton
        type="button"
        aria-label="Chat conversations"
        title="Chat history"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => {
          setSearch("");
          setOpen(!open);
        }}
      >
        <Icon name="menu" className="size-4" />
      </ChatIconButton>
      {open && (
        // Drops down over the conversation, below the Ask panel's header.
        <div
          role="dialog"
          aria-label="Chat history"
          className="absolute top-16 right-2 left-2 z-2 flex max-h-[min(340px,45dvh)] flex-col overflow-hidden rounded-xl border border-line bg-white shadow-[0_12px_35px_#25243c25]"
        >
          <div className="flex shrink-0 items-center gap-2 border-b border-line px-3 py-2.5">
            <svg
              aria-hidden="true"
              className="size-4 shrink-0 text-muted"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.7"
            >
              <circle cx="10.5" cy="10.5" r="7" />
              <path d="m16 16 5 5" />
            </svg>
            <input
              autoFocus
              type="search"
              aria-label="Search recent chats"
              placeholder="Search recent chats"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              className="min-w-0 flex-1 bg-transparent text-[12px] outline-none placeholder:text-muted"
              onKeyDown={(event) => {
                if (event.key === "ArrowDown") {
                  event.preventDefault();
                  root.current
                    ?.querySelector<HTMLButtonElement>("[data-chat-row]")
                    ?.focus();
                }
              }}
            />
            <button
              type="button"
              aria-label="Close chat history"
              className="px-1 text-[17px] text-muted"
              onClick={close}
            >
              ×
            </button>
          </div>
          <div className="flex shrink-0 items-center justify-between gap-2 px-2 py-1.5">
            {/* The browser's own drop-down font and text colour. */}
            <select
              id="chat-session"
              aria-label="Lecture session"
              title="Chats from this lecture"
              className="min-w-0 max-w-[60%] rounded-md bg-transparent px-1 py-1 text-[11px] text-muted"
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
            <button
              type="button"
              className="shrink-0 rounded-md px-2 py-1 text-[11px] font-semibold text-accent hover:bg-accent-soft disabled:opacity-50"
              disabled={
                !session ||
                !health?.chatConversations ||
                !!busy ||
                conversations.loading
              }
              onClick={async () => {
                await onNewConversation();
                close();
              }}
            >
              ＋ New chat
            </button>
          </div>
          <div className="min-h-0 overflow-y-auto overscroll-contain px-1.5 pb-1.5">
            {conversations.loading ? (
              <p className={messageStyle} role="status">
                Loading conversations…
              </p>
            ) : recent.length ? (
              <ul aria-label="Saved conversations">
                {recent.map((item, index) => (
                  <li key={item.id}>
                    <button
                      type="button"
                      className={clsx(
                        itemStyle,
                        "text-ink hover:bg-[#f4f4f8] aria-pressed:bg-accent-soft aria-pressed:text-accent",
                      )}
                      data-chat-row
                      aria-label={item.title}
                      title={item.title}
                      disabled={!!busy}
                      aria-pressed={conversations.current?.id === item.id}
                      onClick={async () => {
                        await onSelectConversation(item.id);
                        close();
                      }}
                      onKeyDown={(event) => {
                        if (
                          event.key !== "ArrowDown" &&
                          event.key !== "ArrowUp"
                        )
                          return;
                        event.preventDefault();
                        const rows =
                          root.current?.querySelectorAll<HTMLButtonElement>(
                            "[data-chat-row]",
                          );
                        const next =
                          index + (event.key === "ArrowDown" ? 1 : -1);
                        if (next < 0)
                          root.current
                            ?.querySelector<HTMLInputElement>("input")
                            ?.focus();
                        else rows?.[Math.min(next, recent.length - 1)]?.focus();
                      }}
                    >
                      <span className="min-w-0 flex-1 truncate">
                        {item.title === "New conversation"
                          ? "Untitled chat"
                          : item.title}
                      </span>
                      <time
                        dateTime={item.updatedAt}
                        title={new Date(item.updatedAt).toLocaleString()}
                        className="shrink-0 text-[10px] font-normal text-muted"
                      >
                        {recentTime(item.updatedAt)}
                      </time>
                    </button>
                  </li>
                ))}
              </ul>
            ) : (
              <p className={messageStyle}>
                {search.trim()
                  ? "No matching chats."
                  : "No conversations in this session yet."}
              </p>
            )}
          </div>
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
  "flex w-full cursor-pointer items-center gap-3 rounded-lg px-2.5 py-2 text-left text-[12px] focus-visible:outline-2 focus-visible:outline-accent";

function recentTime(value: string) {
  const minutes = Math.max(
    0,
    Math.floor((Date.now() - Date.parse(value)) / 60_000),
  );
  if (!Number.isFinite(minutes)) return "—";
  if (minutes < 1) return "now";
  if (minutes < 60) return `${minutes}m`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}h`;
  if (minutes < 10080) return `${Math.floor(minutes / 1440)}d`;
  return new Date(value).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
  });
}
