# 前端可讀性與結構 review（2026-10-01）

範圍：`app-temp/web/src`（不含 `test/` 的內容，只評其位置）。方法：`/human-review`——先由一個沒有讀過任何文件和歷史的 agent 冷讀畫出心智地圖，再逐檔檢查 file purpose 與 slop。本文件只報告，未修改程式碼。Tailwind 遷移計劃見 [tailwind-refactor-plan.zh-HK.md](tailwind-refactor-plan.zh-HK.md)。

## 心智地圖

產品概念（依 [business requirements](../../docs/business-requirements.md)）與現時的家：

| 概念 | 現時的家 | 狀態 |
|---|---|---|
| Session 與 Group（側欄資料庫） | `SessionNav`、`SessionItem`、`handlers.ts`（create／move／delete）、`PlaybackOverlay`（命名 dialog）、`ChatConversationMenu`（第二個 session 選單） | 多個家 |
| Recording（錄音） | 按鈕在 `PlaybackHeader`；polling 與指令在 `handlers.ts:179-249, 470-489`；警告在 `PlaybackPage`；live rows 在 `TranscriptContent` | 沒有家 |
| Transcript | `TranscriptPanel` → `TranscriptContent` → `VirtualTranscript`／`TranscriptRow`／`TranscriptPassage`；時間線模型卻在 `format.ts` | 大致清楚 |
| Audio player | `useAudioPlayback`、`PlaybackFooter`、`PlaybackModeMenu`、`RecordPlay` | 清楚 |
| Notes | `NotesPanel`、`NotePreview`、`NoteTools`、`NoteCoveragePanel`、`NoteEditHistory`（經 Activity 才到）；草稿版本 refs 在 `handlers.ts` | 多個家 |
| Terms | `TermHighlight`、`TermExplanation`、`TermDecisionTrace`、`Component/termSegments.ts`、`handlers.askTerm` | 多個家，跨 folder |
| Ask（問答） | UI 在 `PlaybackOverlay`；邏輯在 `handlers.ask`、`api.askStream`、`useChatConversations`、`ChatAnswer` | 沒有家 |
| AI activity | `useActivity`、`ActivityPopover`、`ActivityContent`、`GroupFlow`、`NotesPanel:86-97` 的狀態推導 | 多個家 |
| Materials（教材） | `TranscriptContent.tsx:222-258` 加 `handlers.attach` | 沒有家 |

入口：`src/` → `pages/PlaybackPage.tsx` 只顯示版面；真正的行為在 `handlers.ts`，但名字不會告訴你。`pages/`、`Component/`、`test/` 都沒有明顯的第一個檔。

追蹤「在側欄選一個 session，transcript 與 notes 出現」：`SessionItem:28` → `action("load", () => refresh(id))`（沒有任何東西叫 selectSession）→ `handlers.ts:80-141`（每次都重新取 health、capture、sessions、groups）→ `sessionSync.ts:54` → `store.ts` → 五個以 session id 為 key 的 effect 分散在 `handlers.ts:69`、`useAudioPlayback:98`、`useChatConversations:10`、`handlers.ts:172`、`useActivity:42` → `NotesPanel`／`TranscriptPanel` 兩棵樹。共經過約 27 個檔。

## Map breaks

- `pages/handlers.ts:20-631`：整個 app 的 controller hook，回傳約 70 個成員，以 `model` 傳入 13 個 component。名字 `handlers` 完全預測不到。
- 「Playback」同時是品牌、app shell（`PlaybackPage`、`PlaybackController`、`PlaybackOverlay`）和 audio player（`useAudioPlayback`、`PlaybackFooter`）。`PlaybackHeader:70-140` 其實是錄音控制。
- `PlaybackOverlay.tsx` 是三件事：整個 Ask 面板（64-157）、全域錯誤（51-58）、session／group 命名 dialog（159-177）。
- `format.ts:57-179` 是 transcript 時間線的領域模型（`TimelineEntry`、`transcriptDays`），不是格式化。
- 找 note 歷史要開第四個檔：`NotesPanel:105` → `ActivityPopover:57` → `ActivityContent:70` → `NoteEditHistory`。版本變更在三處顯示：`NotesPanel:157-196`、`NoteTools` dialog、`NoteEditHistory`。
- 隱藏通道：`handlers.ts:197` 以 `window` CustomEvent 把音量傳到 `PlaybackHeader:34`；`handlers.ts:274` 以另一個 CustomEvent 把「跳到來源」傳到 `VirtualTranscript:73`。不用 grep 找不到聽眾。
- `Component/SourceCitation.tsx:3` import `../pages/format`：通用 component 依賴 feature 檔。`Component/termSegments.ts`、`diagramRenderer.ts` 不是 component。`Component/Markdown.tsx:75-191` 含 notes 的引用與 term 語法，不是通用 renderer。
- API schema 有兩個家：`types/api.ts`，以及 `GroupFlow:7-13`、`NoteTools:7-9`、`NoteCoveragePanel:6-9`、`sessionSync:5-6` 內嵌的 zod。
- 同一個字三個意思：`group`（session 資料夾／`sourceGroups`／Markdown 的 `groups` prop）、`source`（音訊裝置／播放混音／引用）、`record`（錄音／transcript row 的 `RecordPlay`、`record-row`／`recordedAt`）。「Jev」在 UI 從未解釋。
- `pages/` 是一個頁面的 38 個平放檔案：component、hook、store、API client、工具全部混在一起。

## File purposes

只看檔名、folder 與第一屏寫出的猜測，讀完全檔後判定。

| 檔案 | 猜測 | 實際 | 判定 |
|---|---|---|---|
| `handlers.ts` | event handlers | 全 app controller | broke |
| `format.ts` | 格式化工具 | 時間線模型＋ASR 狀態文字＋時間格式 | broke |
| `PlaybackOverlay.tsx` | loading／modal 遮罩 | Ask 面板＋錯誤＋命名 dialog | broke |
| `PlaybackHeader.tsx` | 標題列 | 錄音控制與音量波形 | broke |
| `PlaybackModeMenu.tsx` | live／replay 模式 | 播放音源（混音／單一來源） | broke |
| `RecordPlay.tsx` | 錄音＋播放 | 一行已存音訊的播放鍵 | broke |
| `NoteTools.tsx` | 筆記工具列 | 歷史與恢復 dialog＋整理 section | broke |
| `GroupFlow.tsx` | group 流程圖 | AI agent／execution debug dialog | broke |
| `ActivityContent.tsx` | activity feed | note 編輯紀錄＋term 決策紀錄 | broke |
| `SourceTag.tsx` | 引用標籤 | 咪高峰／系統音訊 badge | broke |
| `TranscriptSettings.tsx` | 逐字稿顯示設定 | ASR 模型、語言、筆記語言、翻譯、term 狀態 | broke |
| `Component/Markdown.tsx` | 通用 Markdown | Markdown＋引用／term 語法 | broke |
| 其餘 `pages/` 與 `Component/` 檔案 | — | — | held |

## Findings（依收益排序）

### [Blocker] 兩個 restore 鍵行為不一致 — `NoteEditHistory.tsx:43`

讀者以為兩處的「Restore」是同一操作。`NoteTools.tsx:79` 送出 `basedOnVersion` 並在完成後 refresh；`NoteEditHistory.tsx:43` 兩者都沒有。後端 `SectionNotes.cs:98` 在 `basedOnVersion` 為 null 時跳過版本衝突檢查，所以從 activity 面板 restore 會蓋過使用者未看到的 AI 新版本（舊版仍在歷史中，不會遺失），畫面要等下一次 4 秒 polling 才更新。

→ 人寫的版本：只保留一個 `restoreNoteVersion(sessionId, version, basedOnVersion)`，放在 `features/notes/`，兩個入口都呼叫它；後端把 `basedOnVersion` 改為必填。

收益：少一條沒有衝突檢查的寫入路徑。

### [Restructure] 全 app controller 經 `model` 傳給每個 component — `handlers.ts:20`

讀者要知道 `NotesPanel` 需要甚麼，必須讀 600 行 `handlers.ts`；子 component 還直接改寫 controller 的 ref（`NotesPanel:259`：`model.editorBaseVersion.current = saved.version`）。

```tsx
// 現在
const model = usePlaybackController();          // 約 70 個成員
<NotesPanel model={model} />
const { session, noteMode, setNoteMode, markdown, setMarkdown, savedMarkdown, busy, jump, action, refresh } = model;
```

```tsx
// 人寫的版本
// pages/PlaybackPage.tsx
const session = useSelectedSession();
<NotesColumn session={session} onOpenSource={openSource} />
<TranscriptColumn session={session} onOpenSource={openSource} />

// features/notes/NotesPanel.tsx
export function NotesPanel({ session, onOpenSource }: NotesPanelProps) {
  const draft = useNoteDraft(session);
  const activity = useActivity(session.id);
  ...
}
```

收益：13 個 component 不再收 `model`；`handlers.ts`（633 行）拆成 `useLibrary`、`useCapture`、`useAsk`、`useNoteDraft` 等各自小於 150 行的 feature hook；每個 component 的依賴從 props 一眼可見。

### [Restructure] 手寫 polling 與「過期 session」防衛重複四次 — `handlers.ts:142-249`、`useActivity.ts`、`useChatConversations.ts`

這是 accretion：每修一個 race 就多一個 ref 或判斷。`handlers.ts` 有 8 處 `usePlaybackStore.getState().session?.id !== id`，加上 `refreshVersion`、`snapshotFlight`、`workspaceFlight`、`questionFlight`；`useChatConversations` 有 `generation` 與 `activeSession`；為了避免重繪，又各自寫了比較函式（`reuseSession`、`sameRecord`、`handlers.ts:214-227` 的 10 欄 capture 比較、`TranscriptContent:290` 的 memo 比較）。

缺少的概念是「以 session 為 key 的伺服器狀態」。

```ts
// 現在（handlers.ts:155，類似判斷共 8 處）
if (controller.signal.aborted || version !== refreshVersion.current
    || usePlaybackStore.getState().session?.id !== id) return;
```

```ts
// 人寫的版本（TanStack Query）
export function useSession(id: string) {
  return useQuery({
    queryKey: ["session", id],
    queryFn: ({ signal }) => syncSession(id, signal),
    refetchInterval: 4000,
  });
}
```

換 session 即換 key，舊請求的結果只會寫回舊 key，不可能覆蓋新 session；structural sharing 內建，取代各個手寫比較函式。不想加 dependency 的話，可以寫一個約 40 行的 `usePolling(key, fetch, ms)` 做同樣的事。

收益：刪除 4 個 flight／version refs、8 處 session 判斷、`reuseSession`、`sameRecord`、capture 比較、memo 比較，約 250 行。新增 dependency 需要你決定。

### [Simplify] 12 個永遠為 true 的 capability flag — `api/Endpoints/HealthEndpoints.cs:15-41`

`sessionSync`、`groundedChatFallback`、`chatConversations`、`recordingSourceSelection`、`sessionAudioMix`、`sessionLanguageSettings`、`sectionNotes`、`noteCoverage`、`aiActivity`、`elapsedRecordingClock`、`manualAsrRetry`、`liveAsrPreview` 都寫死為 `true`。前端仍為 `false` 保留舊路徑和「Restart the API」提示——那是為了新前端連到未重啟的舊 API，而兩者在同一個 repo、同一個 `pnpm dev` 啟動。

```ts
// 現在（handlers.ts:123-125, 345-349）
const synced = h.sessionSync ? await readSession(...) : null;
const item = synced?.session ?? reuseSession(previous, await api("/sessions/" + chosen, ...));
const result = health?.groundedChatFallback ? await askStream(...) : await api(`/sessions/${id}/ask`, ...);
```

```ts
// 人寫的版本
const { session } = await syncSession(id, signal);
const answer = await askStream(id, body, signal, setDraft);
```

收益：12 個 flag 與兩端的分支、第二條 session 取得路徑、4 處「Restart the API」訊息（`PlaybackPage:24-29`、`PlaybackHeader:41-47`、`PlaybackModeMenu:31`、`TranscriptSettings:93`）。動手前先確認 checks 有沒有使用非 stream 的 `/ask`。

### [Restructure] 筆記草稿狀態分散三處 — `handlers.ts:52-53`、`NotesPanel.tsx:254-278`

`markdown` 在 store，`savedMarkdown` 與 `editorBaseVersion` 是 controller 的 ref，由 `NotesPanel` 與 `NoteTools` 直接改寫。Save 的程式碼在 `NotesPanel:255-260` 與 `272-277` 一字不差地重複；「是否有未保存修改」的判斷 `model.markdown !== model.savedMarkdown.current` 寫了 4 次（`NotesPanel:252`、`NoteTools:55, 78, 95`）。`handlers.ts:128-138` 與 `158-162` 又各寫一次「伺服器新版本到達時要不要覆蓋草稿」。

```ts
// 人寫的版本
// features/notes/useNoteDraft.ts —— 使用者正在編輯的文字，以及它基於哪個伺服器版本
export function useNoteDraft(session: Session) {
  ...
  return { text, setText, isDirty, conflict, save, takeServerVersion, recoverPrevious };
}

// NotesPanel
<button disabled={!draft.isDirty || draft.conflict} onClick={draft.save}>Save</button>
```

收益：一條 save 路徑；子 component 不再改寫別人的 ref；衝突處理集中在一個可以單獨測試的 hook。

### [Restructure] 以 window 事件和 CSS class 溝通 — `handlers.ts:197, 270-281`、`VirtualTranscript.tsx:40, 73`

`playback-capture-level` 和 `playback-reveal-source` 兩個 window CustomEvent 是看不見的通道。`handlers.ts:276-281` 尋找 `.timeline-day`／`.timeline-hour`，但沒有任何元素 render 這兩個 class：是死碼（`transcript.css` 對應的規則也是）。另有 14 處執行時依賴 class 名（`captureSelection` 找 `.transcript-row`、`VirtualTranscript` 找 `.transcript-content`、`NotesPanel` 找 `.note-content h1`），換成 Tailwind 後這些 class 會消失。

→ 人寫的版本：`useCapture()` 直接回傳音量歷史，`<LevelMeter levels={capture.levels} />`；「跳到來源」寫入 store 的 `revealTarget`，由 transcript 讀取；DOM 查詢改用 ref 或 `data-*` 屬性。

收益：兩個隱藏通道和一段死碼消失；也是 Tailwind 遷移的前置條件。

### [Simplify] 業務規則藏在 UI 裏 — `NotesPanel.tsx:86-97`、`NotePreview.tsx:29, 38, 43, 49`、`TranscriptContent.tsx:38`

- 筆記狀態是 9 層巢狀三元運算，比對後端 task 名字串（`"Note revision"`、`"Jev note gate"`）和 summary 前綴（`"wait:"`）。
- Term 停用詞 `["details", "detail", "okay", "information"]` 複製了兩次；後端已有 `highlight` 決定，這是在 UI 補丁後端的決定。
- 教材片段 ID 正則 `/^([a-f0-9]{32})_p\d+_\d+_[a-f0-9]{12}$/` 在 `NotePreview` 出現 3 次，對應後端 `MaterialSources.Parent`——就是你指出 `MaterialSources` 難讀的同一件事：把 ID 編成字串再用正則拆回。

```ts
// 人寫的版本
// features/notes/noteStatus.ts
export function noteStatus(session: Session, activity: Activity[]): string | null {
  const running = activity.find(item => isRunningRevision(item, session));
  if (running) return running.draft ? "Editing…" : "Analyzing…";
  if (!hasQueuedSources(session)) return null;
  ...
}
```

Task 名在 `types/api.ts` 變成 `z.enum([...])`；停用詞移到後端的 highlight 決定；API 直接回傳 `{ id, materialId, start, end }`，正則消失。

收益：三條規則各有一個家，`noteStatus` 可以單獨測試。

## 結構

### 現在 → 目標

```text
現在                                   目標
src/                                   src/
  app.tsx, main.tsx                      main.tsx
  Component/                             pages/            只放頁面組合
    Dialog/TextInputDialog.tsx             PlaybackPage.tsx    版面：header、側欄、兩欄、播放列、Ask
    Layout/{Header,Panel,SideNav,          NotesColumn.tsx     notes 一欄（mobile 是一個 tab）
            Workspace}.tsx                 TranscriptColumn.tsx materials＋transcript 一欄
    Icon, LazyDetails, Markdown,         features/         每個產品概念一個 folder
    MathFormula, SourceCitation,           library/        sessions 與 groups 側欄
    diagramRenderer.ts,                    recording/      錄音控制、音量、capture 狀態
    termSegments.ts                        transcript/     時間線模型、虛擬列表、各種 row、設定
  pages/   （38 個平放檔）                  player/         音訊播放
    handlers.ts, store.ts, api.ts, ...     materials/      教材附加與列表
  styles/  （8 個 CSS）                    notes/          閱讀、編輯草稿、歷史與恢復、coverage
  test/    （21 個檢查）                    terms/          高亮、解釋、決策紀錄
  types/api.ts                             ask/            問答、對話、答案
                                           activity/       AI 執行紀錄、group flow
                                           sources/        引用、跳到來源、片段 ID
                                         components/       通用、不知道任何 feature
                                           layout/, Dialog, Menu, Icon, LazyDetails
                                           markdown/       Markdown、數學、Mermaid
                                         lib/
                                           api/            client.ts、schemas.ts
                                           store.ts        只放 UI 狀態
                                           time.ts         時間格式
                                         styles/app.css    Tailwind 入口與 theme
                                       web/test/           檢查移出 src，以保護的行為命名
```

每個 folder 的入口檔：`library/LibrarySidebar.tsx`、`recording/Recorder.tsx`、`transcript/TranscriptPanel.tsx`、`player/AudioPlayerBar.tsx`、`materials/MaterialsList.tsx`、`notes/NotesPanel.tsx`、`terms/TermHighlight.tsx`、`ask/AskPanel.tsx`、`activity/ActivityPopover.tsx`、`sources/SourceCitation.tsx`。

關於你提出的 `page.tsx`／`LeftSection`／`RightSection`：同意 `pages/` 只放頂層組合。兩欄建議以內容命名（`NotesColumn`、`TranscriptColumn`），因為 mobile 上它們是 tab 而不是左右。若一欄只包一個 feature panel，它會是 pass-through，可直接在 `PlaybackPage` 組合；目前 transcript 欄包含 materials＋transcript，值得獨立一檔。

### 搬移清單

每一步都不改行為，修正的 map break 列在後面。

1. 拆 `handlers.ts` → `library/useLibrary.ts`、`recording/useCapture.ts`、`ask/useAsk.ts`、`notes/useNoteDraft.ts`、`materials/attachMaterial.ts`、`sources/useRevealSource.ts`；刪 `handlers.ts` 與 `PlaybackController` type。（god controller、`model` prop）
2. `format.ts` 拆成 `transcript/timeline.ts`（`TimelineEntry`、`transcriptDays`，併入 `transcriptPassages.ts`）、`transcript/asrStatus.ts`、`lib/time.ts`。（format 名不副實）
3. `PlaybackOverlay.tsx` 拆成 `ask/AskPanel.tsx`、`components/ErrorToast.tsx`、`library/NameDialog.tsx`。（三件事）
4. `PlaybackHeader.tsx` → `pages/PlaybackPage` 的 header 部分＋`recording/Recorder.tsx`＋`recording/LevelMeter.tsx`。（Playback 三義）
5. 改名：`PlaybackFooter` → `player/AudioPlayerBar`、`PlaybackModeMenu` → `player/AudioSourceMenu`、`useAudioPlayback` → `player/useAudioPlayer`、`RecordPlay` → `player/PlayButton`、`SourceTag` → `recording/AudioSourceBadge`、`NoteTools` → `notes/NoteHistoryDialog`（organize 移到 `notes/OrganizeSection.tsx`）、`GroupFlow` → `activity/AiFlowDialog`、`ActivityContent` → `activity/ActivityLog`、`TranscriptSettings` → `transcript/SessionSettings`、`TranscriptContent` → `transcript/Timeline`、`VirtualTranscript` → `transcript/VirtualList`。（file purpose broke）
6. `TranscriptContent.tsx:222-258` → `materials/MaterialsList.tsx`。（materials 沒有家）
7. `TranscriptContent.tsx:92-218` 的 `renderEntry` 拆成 `transcript/rows/{LiveRow,SilenceRow,AudioRow}.tsx`，與 `TranscriptRow`、`PassageRow` 並列。
8. `Component/termSegments.ts` → `terms/termMatching.ts`；新增 `terms/visibleTerms.ts`（停用詞與語言過濾的唯一的家，之後移去後端）。（跨 folder、重複）
9. `Component/SourceCitation.tsx`、`pages/SourceLinks.tsx` → `sources/`；片段 ID 解析集中到 `sources/passageId.ts`，之後改為 API 回傳結構。（通用依賴 feature、正則 ×3）
10. `Component/Markdown.tsx` 拆出 notes 引用語法 → `notes/noteMarkdown.tsx`；`Markdown`、`MathFormula`、`diagramRenderer` → `components/markdown/`。（Markdown 名不副實）
11. `pages/api.ts` → `lib/api/client.ts`，參數改為具名：`api.get(path, Schema, { signal })`、`api.post(path, body, Schema)`；`types/api.ts` 與 4 處內嵌 zod → `lib/api/schemas.ts`。（schema 兩個家）
12. 側欄、Ask、播放、activity 等 8 處各自寫的「點外面／Esc 關閉」→ `components/Menu.tsx`。
13. `Component/` → `components/`（小寫、與其他 folder 一致）；`Layout/` → `components/layout/`。
14. `src/test/` → `web/test/`；以保護的行為命名（`notes-redesign-check` → `notes-reading-sources.check.mjs` 等）。
15. 刪：`store.ts` 的 `transcriptView`（`"activity"` 從未被設定）、`handlers.ts:276-281` 與 `transcript.css` 的 `.timeline-day/.timeline-hour`、12 個 capability flag（finding 4）。

## Slop inventory

| Pattern | 位置 | 次數 |
|---|---|---|
| 同一 session 過期判斷（accretion） | `handlers.ts` | 8 |
| flight／version／generation／request refs | `handlers.ts` 4、`useChatConversations` 2、`NoteTools`、`NoteCoveragePanel`、`TermExplanation` | 9 |
| 手寫「相同則沿用舊物件」比較 | `sessionSync`、`useActivity`、`handlers`、`TranscriptContent`、`PlaybackFooter`、`NotePreview` | 6 |
| 永遠為 true 的 flag 分支 | 前端 20 處引用 9 個 flag；另 3 個（`elapsedRecordingClock`、`manualAsrRetry`、`liveAsrPreview`）前端從未讀取 | 12 個 flag |
| 每行超過 140 字 | 最多：`TranscriptContent` 18、`NoteTools` 17、`GroupFlow` 12、`Markdown` 10、`NotePreview` 9、`NotesPanel` 8 | 137 |
| 內嵌 zod schema | `GroupFlow`、`NoteTools`、`NoteCoveragePanel`、`sessionSync` | 4 |
| 自寫「點外面關閉」／`closest("details")` | `SessionNav`×3、`SessionItem`×2、`PlaybackModeMenu`、`ChatConversationMenu`、`ActivityPopover` | 8 |
| 執行時依賴 CSS class | `handlers`、`NotesPanel`、`VirtualTranscript`、`NotePreview` | 14 |
| 未命名的業務數字 | `sessionSync:64,70`（334、24000、8000000、8、200）、`useAudioPlayback`（30_000）、`SourceCitation`（300000）、`TranscriptContent:82`（60000、30000） | 10 |
| 重複的 helper | `time`（`format.ts`、`NoteCoveragePanel`）、停用詞 ×2、片段正則 ×3 | 3 |

## 刻意保留

- `useAudioPlayback.ts`：真正的 deep module。介面細（toggle、seek、skip、chooseSource、chooseSpeed），隱藏了 30 秒音訊段、佇列、快取與跨段播放。只搬家改名。
- `PlaybackModeMenu.tsx`：props 型別明確、一件事。是其他 component 應該學的樣子；只把關閉邏輯換成共用 `Menu`。
- `TranscriptRow.tsx`、`RecordPlay.tsx`、`SourceTag.tsx`、`LazyDetails.tsx`、`Layout/*`：props 明確、單一職責。只改名搬家。
- `VirtualTranscript.tsx`：複雜但有理由——三小時課堂約 1,266 行。保留演算法，只移除 window 事件和 class 依賴。
- `sessionSync.ts` 的分頁與 fragment 協定：是真實概念。保留，只為數字命名、一行一句。
- `api.ts` 的 timeout、大小上限與錯誤訊息：是 AGENTS.md 要求的邊界防護，保留。
