import type { Session } from "../../lib/backend/schemas";

// The editable instructions offered for a first draft: the usual scope of meeting minutes without a template, or a
// reminder to follow the template's structure with one. The owner's own edit is remembered in this browser.

const withoutTemplate: Record<Session["noteLanguage"], string> = {
  "zh-Hant": `請撰寫一份正式、可直接交給客戶的會議紀錄，包括：
1. 標題：會議名稱、日期、時間、地點（未知的寫 [TBC]）
2. 出席者：姓名及所屬機構／職位（表格）
3. 會議摘要：3–5 點主要結果
4. 討論事項：按議程逐項簡述
5. 決定事項
6. 問題與澄清：提出的問題、回覆及狀態（已解答／待跟進）
7. 跟進事項：負責方、事項、期限（表格）
8. 下次會議安排（如有）
語氣正式、簡潔、客觀，不記錄閒聊，不加入會議中沒有提及的內容。`,
  "zh-Hans": `请撰写一份正式、可直接交给客户的会议纪要，包括：
1. 标题：会议名称、日期、时间、地点（未知的写 [TBC]）
2. 出席者：姓名及所属机构／职位（表格）
3. 会议摘要：3–5 点主要结果
4. 讨论事项：按议程逐项简述
5. 决定事项
6. 问题与澄清：提出的问题、回复及状态（已解答／待跟进）
7. 跟进事项：负责方、事项、期限（表格）
8. 下次会议安排（如有）
语气正式、简洁、客观，不记录闲聊，不加入会议中没有提及的内容。`,
  en: `Write formal meeting minutes, ready to send to a client, covering:
1. Title: meeting name, date, time and venue (write [TBC] for anything unknown)
2. Attendees: name and organisation or role (table)
3. Summary: 3–5 key outcomes
4. Discussion: a short account of each agenda item
5. Decisions
6. Questions and clarifications: each question, the answer and its status (answered / open)
7. Action items: owner, action and deadline (table)
8. Next meeting, if one was set
Keep it formal, concise and factual; leave out small talk and add nothing that was not said in the meeting.`,
};

const withTemplate: Record<Session["noteLanguage"], string> = {
  "zh-Hant":
    "請按模板的章節、格式和語氣撰寫本次會議紀錄。模板中屬於上次會議的內容不要沿用；資料不足的地方寫 [TBC]。",
  "zh-Hans":
    "请按模板的章节、格式和语气撰写本次会议纪要。模板中属于上次会议的内容不要沿用；资料不足的地方写 [TBC]。",
  en: "Follow the template's sections, layout and tone for this meeting. Do not reuse the earlier meeting's content in the template; write [TBC] where details are missing.",
};

const key = (language: string, templated: boolean) =>
  `playback.export.firstDraft.${templated ? "template" : "free"}.${language}`;

export function defaultFirstDraft(
  language: Session["noteLanguage"],
  templated: boolean,
) {
  return (
    (templated ? withTemplate : withoutTemplate)[language] ?? withoutTemplate.en
  );
}

// The owner's saved version, else the default.
export function firstDraftPrompt(
  language: Session["noteLanguage"],
  templated: boolean,
) {
  try {
    const saved = localStorage.getItem(key(language, templated));
    if (saved?.trim()) return saved;
  } catch {
    /* Storage can be unavailable; the default still works. */
  }
  return defaultFirstDraft(language, templated);
}

export function rememberFirstDraft(
  language: Session["noteLanguage"],
  templated: boolean,
  text: string,
) {
  try {
    if (text.trim() && text !== defaultFirstDraft(language, templated))
      localStorage.setItem(key(language, templated), text);
    else localStorage.removeItem(key(language, templated));
  } catch {
    /* Not remembered; nothing else depends on it. */
  }
}
