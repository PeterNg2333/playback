# 2026-09-28：筆記、短引用及對話更新

本文件記錄本輪新行為，優先於 ai-flow／README 中較早的 3.5 live 測試記錄。重啟 API 並重載 UI；現有筆記按 Revise with AI 才按新 prompt 改寫。不需清空資料庫。

## 內容與來源

- 筆記 prompt 要求短 takeaways、每題 2–4 個要點，按實際來源需要使用 tree、table 或 Mermaid 圖。topic tree 可跳至主題；不為了裝飾捏造關係。
- Live draft 是獨立分頁，標明引用尚未驗證、未保存；紫色 pen badge 在標題旁顯示 Analyzing／Editing。保留 Preview、未保存 editor/caret 和讀者位置。
- 同 source 的相鄰音訊合為最長約 60 秒、最多 16 chunks 的來源段落。模型用 `01`、`02`，教材用 `M01`；保存前展開回原始 IDs。錄音及 ASR chunk 身份保留，播放以既有 queue 串起整段。
- UI 把每段重複引用收成短標籤；點開查看來源、播放整段或跳到個別時間。未知長 ID 顯示 Source unavailable，不猜替代來源；code／外部連結保留。
- 翻譯批次 target 亦用 `01` 等 alias，本機映射回原 transcript IDs；鄰近 context 用 `C01`，不輸出 context 譯文。JSON target 檢查仍拒絕未知、漏掉或重複 targets。

## 詞語與聊天

- 新來源自動觸發 Jev 判斷；通過現有門檻後 Gemini＋Search 生成並保存短解釋。啟動亦掃描最近最多 128 個 session 的未處理詞；有界重試，實際失敗顯示在 Activity。
- 筆記／逐字稿粗體高亮詞；hover／focus／tap 開啟保存的解釋和 Ask a follow-up in chat。popup 只讀，不觸發付費請求。移除 UI 的手動 Review 按鈕。
- 詞解釋不再追加長筆記段落或新增 note version；下一次筆記 revision 要移除舊的明確標示 AI/web 補充段落。外部解釋與課堂證據分開。
- Chat 寬 555 px（原本 1.5 倍）、高度上限 1120 px（原本 2 倍），實際高度限制在 viewport 內；英文 UI／短要點答案，明確要求其他語言時可轉換。Enter 送出，Shift+Enter 換行；失敗保留輸入。
- 標題旁選單按 Group 整理 Lecture session，可開新對話／重開已保存對話。**Group 只整理選單，每段對話只引用所選講堂。** MongoDB 的 conversations／conversation_turns 保存已驗證完整答案；最多四個 prior turns 作 context，不能當作新證據。

## 模型與成本

Routine 預設 `gemini-3.1-flash-lite`；process environment 的 `PLAYBACK_GEMINI_MODEL` allowlist 為 3.1／3.5 Flash-Lite，health 顯示實際模型。沒有新增自動 3.8 advisor 呼叫。[Google model documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-1-flash-lite) 支援目前使用的能力；本輪未作新的付費 key／endpoint 驗證。

自動筆記每 8 秒掃描，但首次及後續自動嘗試至少相隔 90 秒；手動 Revise 即時執行。輸出上限：筆記／翻譯 4096 tokens、Q&A／其他 agent 1024、grounded term 768。token-limit completion 拒絕保存截斷結果。Q&A 預設 3–5 bullets／180 words，詞解釋預設定義加最多兩點／90 words。

穩定 instructions／來源 prefix 放前，變動問題或 note version 放後，配合 [Google implicit caching](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/context-cache/context-cache-overview)。沒有建立 explicit cache resource，未測量 provider cache hit 或費用減幅。成功 Q&A 的完全相同模型／問題／來源 context 可在本機重用：private 30 分鐘、web 5 分鐘，最多 128 entries；失敗不進這個 cache。有 conversation ID 的已完成同 request ID／payload 亦可於 API 重啟後重用。

**筆記仍生成整份內容，section-only patch 未實作。** 普通 agent 的 token usage 尚未保存，不能重建完整帳單；未把使用者提供的費用／幣別分析當作本輪實測。

## 離線與本機驗證

```powershell
dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/ui-notes-checks/net10.0/
dotnet app-temp/checks/bin/ui-notes-checks/net10.0/Playback.Checks.dll
dotnet app-temp/checks/bin/ui-notes-checks/net10.0/Playback.Checks.dll --conversation-check
npm.cmd --prefix app-temp/web run build
# 另開本機 Vite 5174：npm.cmd --prefix app-temp/web run dev -- --port 5174 --strictPort
node app-temp/web/src/test/notes-chat-check.mjs
node app-temp/web/src/test/asr-ui-check.mjs
```

Protocol 檢查涵蓋短 alias、合併段落完整來源、三小時早／晚來源檢索、翻譯身份、未知引用及輸出截斷。Conversation check 只連 localhost MongoDB 的 playback_e2e，自建兩個 session 後清理自己資料，驗證保存、Session 隔離、獨立對話、重啟重用及 payload 身份；不清空 DB，不呼叫 provider。

瀏覽器 fixture 攔截 API 並封鎖外部 HTTPS，驗證 Markdown／diagram、hover、Live draft、未保存編輯、聊天保存／切換／reload、網絡與 provider 失敗、重複 Send，以及 320–1440 px 和 125% zoom。截圖在本機 output/playwright/。這些檢查不代表自然課堂生成品質或三小時實機穩定性。

本輪亦以記憶體 HTTP fixture 經真正 Google SDK／Agent Framework 驗證 maxOutputTokens 請求與串流／非串流截斷拒絕；本機短 WAV 證實 combined passage 依序播放全部 chunks。來源及 term popup 在播放器／背景刷新時保持開啟。聊天 history 只送有長度上限的文字及短引用，省略已保存的 evidence arrays 和 provider metadata。

Docker 工作交由 [另一個 session 的 prompt](../../docs/docker-compose-handoff.md)；本輪沒有啟動容器、修改 .env、清空資料庫或提交 Git。
