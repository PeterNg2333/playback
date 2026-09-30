import { useEffect, useRef, useState } from "react";
import type { PlaybackController } from "./handlers";
import { Icon } from "../Component/Icon";

export function ChatConversationMenu({ model }: { model: PlaybackController }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => { if (!root.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [open]);
  return <div className="chat-conversation-picker" ref={root}>
    <button className="icon-button" type="button" aria-label="Chat conversations" aria-expanded={open} onClick={() => setOpen(!open)}>
      <Icon name="menu" />
    </button>
    {open && <div className="chat-conversation-menu">
      <label htmlFor="chat-session">Lecture session</label>
      <select id="chat-session" value={model.session?.id ?? ""} disabled={!!model.busy || model.chatHistory.loading}
        onChange={event => { void model.refresh(event.target.value).catch(error => model.setError(error.message)); }}>
        {!model.session && <option value="">Select a lecture session</option>}
        {model.groups.map(group => <optgroup key={group.id} label={group.name}>
          {model.sessions.filter(item => item.groupId === group.id).map(item => <option key={item.id} value={item.id}>{item.title}</option>)}
        </optgroup>)}
        <optgroup label="Ungrouped sessions">
          {model.sessions.filter(item => !item.groupId).map(item => <option key={item.id} value={item.id}>{item.title}</option>)}
        </optgroup>
      </select>
      <small>Answers use only this lecture session.</small>
      <button type="button" className="new-conversation" disabled={!model.session || !model.health?.chatConversations || !!model.busy || model.chatHistory.loading}
        onClick={async () => { await model.newConversation(); setOpen(false); }}>＋ New conversation</button>
      {model.chatHistory.loading ? <p role="status">Loading conversations…</p>
        : model.chatHistory.list.length ? <ul aria-label="Saved conversations">
          {model.chatHistory.list.map(item => <li key={item.id}><button type="button" disabled={!!model.busy}
            aria-pressed={model.chatHistory.current?.id === item.id}
            onClick={async () => { await model.selectConversation(item.id); setOpen(false); }}>{item.title}</button></li>)}
        </ul> : <p>No conversations in this session yet.</p>}
      {model.health && !model.health.chatConversations && <small>Restart the API to enable saved conversations.</small>}
    </div>}
  </div>;
}
