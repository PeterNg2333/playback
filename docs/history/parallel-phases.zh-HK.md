# 平行工作 phase prompts（2026-10-19）

來源：[下一個 session 清單](../next-session-checklist.zh-HK.md)。每個 phase 一個 session、一個 git worktree、一條 branch，可以同時做。同一個 wave 入面嘅 phase 擁有唔重疊嘅檔案；下一個 wave 要等上一個 wave 合併入 `main` 先開始。

| Wave | Phase | 做咩 | 收費／上網 |
| --- | --- | --- | --- |
| 1 | P1 | Web build、browser checks、實機錄音冒煙測試 | 實機一段（要授權） |
| 1 | P2 | 長課堂同廣東話 replay；比較 runner 加 OpenCC | 要授權 |
| 1 | P3 | Organizer 範圍、自動 subtopic 標題 | 離線 |
| 1 | P4 | OpenRouter streaming、失敗 fallback、OpenCC 詞組 | 離線（live 驗收另授權） |
| 1 | P5 | DB／儲存遷移分階段計劃（只寫文件） | 只讀官方文件 |
| 1 | P6 | 研究：講者分離、文件文字抽取 | 只讀官方文件 |
| 2 | P7 | Prompt profile 同會議模式 | 離線 |
| 2 | P8 | 上載 PDF／PPT／Word 做 materials | 離線 |
| 2 | P9 | DB 遷移第 1 階段：users／sessions | 要批准清 DB |
| 2 | P10 | FAQ mode | 離線 |
| 3 | P11–P13 | DB 第 2–3 階段、Login、講者分離實作 | 見各 prompt |
| 收尾 | M | 合併後更新清單同 README | — |

## 進度（2026-10-20）

| Phase | 狀態 | 已入 main | 下一步 |
| --- | --- | --- | --- |
| P1 | 已測；live 未完全達標 | 本輪提交 | [結果](phases/p1-results.zh-HK.md)：Web／browser 綠；首 note 132 秒；placement schema 已修，in-memory live diagnostic 成功，原 Mongo 兩段仍 pending |
| P2 | 已有部分長測／廣東話證據；完整雙模型未完成 | 本輪提交 | [結果](phases/p2-results.zh-HK.md)：Gemini 109 分鐘約 US$1.95 後停；Qwen 約133分鐘取消、未保存 note；全部 paid runs 停止，不加預算 |
| P3 | 完成，已入 main；M status 修正待提交 | `d4c0804` | Storage／MemoryStore 已加入 source 時間投影，time-full status check 通過。六係 review 門檻，writer schema 同步容許相關內容留喺 full topic |
| P4 | 完成，已入 main；有跟進 P4b | `f17f77e` | 已做：streaming、Qwen → GLM → Qwen → Gemini fallback（逐個 model 一個 request）、Activity 記錄實際 model／provider、「上周／周末」唔強制轉。剩低四個決定，見下面「P4b」 |
| P4b | 決定落實，live 有限制 | 本輪提交 | [結果](phases/p4b-results.zh-HK.md)：US$0.07850672；手動 recovery 後 58/58，唔係連續完整 run；organizer 未證品質 |
| P5 | 文件完成 | 本輪提交 | [分階段遷移計劃](../storage-migration-plan.zh-HK.md)，冇改 DB／部署 |
| P6 | 研究完成 | 本輪提交 | [結果](phases/p6-results.zh-HK.md)：diarization／document extraction，冇安裝／整合 |

已入 main 嘅行為：
- OpenRouter 筆記 streaming，草稿約早 5 秒出現。
- Qwen 撞可重試錯誤即刻轉 GLM 5.3 Flash，再試 Qwen，最後 Gemini。
- OpenCC 共用字詞組轉換。
- Organizer 用 `moves`／`topicMoves`，一定要寫目標。
- Writer 宣告 subtopic，取代句子標題。
- 六係 review 門檻，唯一硬上限係三層深度；第三層超過六個 subtopic 會 review，可以合併或者唔郁。

已知事項：
- `qwen/qwen3.8-flash` 喺 OpenRouter 只有 Alibaba 一個 provider，繁忙時會 429（`limit_burst_rate`，共用容量）。OpenRouter `models` 清單唔可以用：Qwen 要 reasoning `none`，GLM 唔准關 reasoning。
- P2 要知：比較 runner（`NoteModelComparison.cs`）而家用 streaming，亦有 P3 加嘅 429 停止開關（`PLAYBACK_NOTE_MODELS_STOP_ON_429=yes`）。
- Live run 資料（git 唔追蹤）：`app-temp/data/validation/runs/2026-10-19-p3-organize-*`、`2026-10-20-p4-qwen-stream`。
- `.claude/worktrees/` 仲有 6 個 worktree：4 個係開 subagent 失敗留低嘅空 worktree，另外 2 個係 P3、P4 已合併嘅 worktree。清理要使用者批准。

Wave 1 收尾見 [目前結果同未達標項目](phases/wave1-closeout.zh-HK.md)。使用者指定之後優先 cloud deployment／accounts；P7 prompt profile、FAQ 等 Wave 2 功能唔自動開始。P1 latency／完整 live E2E 同 P2 完整雙模型比較未驗收，不能把收尾文件當成全部 acceptance 綠。

### 明日繼續：統籌 session

```text
你係統籌，唔親自寫 code。讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「進度」一節同「共同規則」。

今日要做（照次序，每項開始前問我要唔要做）：
1. 清 worktree：跑 git worktree list，列出 .claude/worktrees 入面每個 worktree 嘅 branch、有冇本輪提交改動、branch 係咪已經合併入 main、有冇 git 唔追蹤嘅 run folder 未抄去 main checkout。等我批准先清。
2. P4b：用本檔案「P4b」嘅 prompt（我會先填好決定）開一個 subagent（Agent tool，isolation: "worktree"，背景執行）。
3. P5、P6：用 Wave 1 一節嘅原文 prompt 各開一個 subagent。上次 auto mode 擋咗呢兩個 Agent call；如果再被擋，唔好繞過，同我講。
4. P1、P2 要我睇住做，唔好交俾背景 subagent。我叫先開，到時喺呢個 session 直接做。

每個 subagent 嘅 prompt 後面加：「你喺 worktree 入面，冇得中途問使用者；遇到 prompt 叫你『先問我』嘅決定，就用 prompt 入面已答嘅答案；冇答案就停喺嗰度，報告問題同你建議嘅答案。」

每個 subagent 完成或者停低時，用三行話同我講結果（branch、完成條件達到未、要我決定嘅嘢）。我答完之後，用 SendMessage 叫返同一個 subagent 繼續。唔好自己合併 branch，唔好 push，除非我叫。

全部完成後：列出每條 branch 嘅 commit、結果文件同範圍外改動，建議合併次序，再更新本檔案「進度」一節。
```

### P4b：GLM fallback 收尾

貼之前先填好「已答嘅決定」。

```text
你負責 Playback 嘅 phase P4b。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p4b-glm-fallback（由 main 開）。

背景：main 已有 P4（f17f77e）。筆記 writer 逐個 model 發 request：Qwen（reasoning none）撞可重試錯誤即刻轉 GLM 5.3 Flash（reasoning low），再試 Qwen，最多 3 次 OpenRouter 嘗試、60 秒內，之後 Gemini。詳情同 live 數據見 docs/phases/p4-results.zh-HK.md。

已答嘅決定：
1. 逐個 model request 取代 OpenRouter models 清單：<接受／其他>
2. GLM request 嘅數據政策：<預設／data_collection: deny／zdr: true>
3. Stream 45 秒冇數據當斷線：<維持 45 秒／改做 N 秒>
4. 用比較 runner 跑一次 GLM 筆記：<批／唔批>

擁有：app-temp/infrastructure/Providers/OpenRouterTextModel.cs、app-temp/infrastructure/Notes/**、app-temp/checks/Offline/OpenRouterTextChecks.cs、NoteWriterFallbackChecks.cs、STRUCTURE.md 嘅 Notes 同 Note script 兩行、docs/phases/p4b-*。

步驟：
1. 第 1 項唔係「接受」就停低報告，唔好改。
2. 第 2 項唔係「預設」：先用 OpenRouter 免費公開 endpoints 查詢，確認 z-ai/glm-5.3-flash 加咗限制之後仍然有支援 structured output、fp8 或以上嘅 provider。冇就停低報告。有就加入 GLM request，加離線 check 檢查 request body。結果文件寫低數據去邊。
3. 第 3 項改咗秒數就更新常數同對應離線 check。
4. 第 4 項批咗：用 npm run test:note-models-live（或者 runner 直接用 dotnet）跑 openrouter:z-ai/glm-5.3-flash@low，Week 3 頭 20 分鐘 batch+organize 一次。量度：來源覆蓋、繁體字（模型原始輸出同 OpenCC 之後分開報告）、organizer 套用／丟棄數、每次 call 延遲、費用。同 docs/phases/p4-results.zh-HK.md 嘅 Qwen run 比較，結論寫明 GLM 夠唔夠做第二層。

完成條件：build 同全部離線 checks 綠；每個已答決定有對應改動同 check，或者寫明唔使改嘅原因；結果寫入 docs/phases/p4b-results.zh-HK.md。

授權：可以 commit 到 phase/p4b-glm-fallback（唔可以 push）。可以做免費嘅 OpenRouter endpoints 查詢。第 4 項批咗嘅話，可以跑一次 GLM 比較 run，上限 US$0.05，失敗最多重跑一次；頭 10 個 writer call 有 3 個 429 就停。
```

### M 收尾要處理嘅項目（Wave 1）

- P3：Storage／`MemoryStore` 嘅整理狀態顯示未計「按時間滿」。
- 各結果文件「範圍外改動」一節：P3 改咗 `ModelOutputContract.cs`（P4 擁有）、`NoteSections.cs`、`NoteWorkflow.cs`、`NoteModelComparison.cs`（P2 擁有）、`checks/Program.cs`；P4 改咗 `checks/Program.cs`。
- 套件管理：使用者話用 pnpm，但 repo 用 npm（`app-temp/web/package-lock.json`，`setup` 用 `npm ci`）。等使用者決定要唔要轉。

---

用法：喺 Claude Code 開新 session，用 worktree（例如叫佢 `EnterWorktree`，或者 `git worktree add ../playback-p3 -b phase/p3-organizer-scope main`），然後貼上對應 phase 嘅 prompt。每個 prompt 會叫 agent 讀本檔案嘅「共同規則」，路徑用 main checkout 嘅絕對路徑，所以本檔案未 commit 都讀得到。

亦可以喺一個統籌 session 用 subagent 一次過開成個 wave，見下面「統籌 session」。

每個 prompt 最尾有「授權」一段：嗰啲句子就係你嘅明確授權（commit 到自己 branch、live 測試等）。唔想授權就刪走嗰句先貼。

---

## 統籌 session

```text
你係統籌，唔親自寫 code。讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md，然後為 Wave <N> 嘅 <P3、P4、P5、P6> 各開一個 subagent（Agent tool，isolation: "worktree"，背景執行），同時開出去。每個 subagent 嘅 prompt 就係檔案入面該 phase 嘅 text block 原文，再加：「你喺 worktree 入面，冇得中途問使用者；遇到 prompt 叫你『先問我』嘅決定，就用下面已答嘅答案；冇答案就停喺嗰度，報告問題同你建議嘅答案。」

已答嘅決定：
- P2 廣東話樣本：<有，路徑係 … ／ 冇，用合成 fixture>
- P4 fallback：<(2) 可重試錯誤用完重試後改用 Gemini>

每個 subagent 完成或者停低時：用三行話同我講結果（branch、完成條件達到未、要我決定嘅嘢），有問題就轉問我，答完用 SendMessage 叫返同一個 subagent 繼續。唔好自己合併 branch，唔好 push。全部完成後列出每條 branch 嘅 commit、結果文件同範圍外改動，建議合併次序。
```

尖括號入面嘅嘢先填好先貼。P1 嘅實機錄音要用你部機嘅咪同瀏覽器，同 P2 嘅收費 live 測試一樣，最好喺你睇住嘅 session 做，唔好交俾背景 subagent。

---

## 共同規則（每個 phase 都要跟）

1. 開工前讀 `AGENTS.md`、`app-temp/STRUCTURE.md`、`docs/next-session-checklist.zh-HK.md`、`app-temp/README.zh-HK.md`，同埋你 phase 擁有嘅檔案同對應 checks。睇 `git status`，保留本輪提交嘅工作。
2. 你喺自己嘅 worktree 同 branch（`phase/<id>-<名>`，由 `main` 開）工作。其他 phase 同時改緊其他檔案：**只改你 phase「擁有」一欄列出嘅檔案同你新增嘅檔案**。真係要改範圍外檔案，就將改動縮到最細，並喺結果文件「範圍外改動」一節逐個列出原因，等合併嗰個人處理衝突。
3. 唔好改 `docs/next-session-checklist.zh-HK.md`、`AGENTS.md` 同本檔案；合併步驟（M）會統一更新。你嘅結果寫喺 `docs/phases/<id>-results.zh-HK.md`：做咗咩、證據（指令同輸出）、未完成、限制、範圍外改動。
4. 改 prompt 文字就升 prompt 版本號（例如 `tree-notes-v13` → `v14`），令保存結果分得清新舊版本。
5. 完成條件：`dotnet build`、`dotnet run --project app-temp/checks`（全部離線 checks）都通過；改咗 web 就加跑 `npm.cmd --prefix app-temp/web run build` 同受影響嘅 browser checks（指令見 `STRUCTURE.md`）。新行為要有自己嘅離線 check，check 檔案以佢保護嘅產品概念命名。失敗就照實報告，唔好用 mock 成功代替。
6. Live／收費步驟只喺 prompt「授權」一段寫明時先做，而且照寫明嘅規模。冇授權就完成離線部分，再喺結果文件寫低精確嘅 live 測試計劃（指令、請求數、估計費用）。
7. 有歧義就先問一條精簡問題，同時繼續做唔受答案影響嘅部分。

---

## Wave 1

### P1：Web 驗收同實機冒煙測試

```text
你負責 Playback 嘅 phase P1。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p1-smoke。

目標：確認 commit 3cecbe0（Qwen3.8 Flash 做預設筆記模型、編輯操作、placement harness）喺 web 同真實流程都 work。

擁有：app-temp/web/test/**、app-temp/web/src/features/activity/**；只喺 check 揭出 bug 時先改其他 web 檔案。

步驟：
1. 跑 web build 同全部離線 browser checks（STRUCTURE.md「Web」一節列出嘅 checks；markdown-math 要用 production preview）。Activity 顯示 provider 嘅來源改咗：確認 Activity popover 顯示到 openrouter／qwen 同 vertex／gemini 兩種 writer。冇 check 覆蓋就加一個。
2. 修好你搵到嘅失敗，每個修正有 check 支持。
3. 實機冒煙測試（要授權）：npm run dev，開新 session，用咪講或者播 app-temp/data/test-audio/sampleAudio/sampleAudio.m4a 嘅一段約 3 分鐘，走完錄音 → ASR → Qwen 筆記 → organizer → Stop。記錄：Activity 顯示嘅 provider／model、第一段筆記喺講完之後幾多秒出現、organizer 有冇觸發、有冇錯誤、筆記繁體字、引用跳返 transcript work 唔 work。影 desktop 同 375px 截圖檢查。

完成條件：全部離線 browser checks 綠；實機測試每一項有量度值或者截圖；結果寫入 docs/phases/p1-results.zh-HK.md。

授權：可以 commit 到 phase/p1-smoke（唔可以 push）。可以啟動本機 MongoDB container（npm run db:up），可以做一次約 3 分鐘嘅實機錄音測試，包括上傳音訊去 ASR 同呼叫筆記模型。
```

### P2：長課堂同廣東話 replay

```text
你負責 Playback 嘅 phase P2。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p2-long-replay。

目標：量度 Qwen 筆記 writer 喺完整三小時級課堂同廣東話課堂嘅表現，作為之後調整嘅基準。背景：docs/note-model-comparison-2026-10-18.md。

擁有：app-temp/checks/Validation/**、app-temp/data/validation/runs/<你新開嘅 folder>、docs/phases/p2-*。

步驟：
1. 比較 runner（NoteModelComparison.cs、run-note-models.mjs）而家量度模型原始輸出，冇經 OpenCC。加一個選項傳 note script，令 runner 可以量度最終保存嘅筆記；預設行為唔變。加離線 check。
2. 長課堂 replay（要授權）：用完整 app-temp/data/test-audio/sampleAudio/transcript.txt，Qwen batch+organize。NoteInput 嘅 lecture window 上限 200,000 字，超過後最舊嘅講話會移出，cache prefix 會變。量度：每次 call 嘅延遲、input／cached tokens、cache 命中率隨時間嘅變化、總成本、來源覆蓋（cited/total）、organizer 觸發次數同被丟棄嘅操作數。
3. 廣東話 replay（要授權）：repo 冇廣東話課堂。先問我有冇廣東話錄音或者逐字稿；冇嘅話用 prepare-validation.ps1 同 checks 嘅 --validation-fixtures 整合成廣東話／中英夾雜 fixture，喺報告清楚寫明係合成。檢查分組、「(?)」標記、繁體字（包括「示范」呢類全部由共用字組成嘅詞）、粵語口語有冇被改寫成書面語或普通話。
4. 人工抽查至少 10 段筆記同原文對照，列出錯誤例子。

完成條件：每項量度有數字同 run folder 連結；報告分開「模型原始輸出」同「OpenCC 之後」；結論列出要 P3／P4 跟進嘅具體問題。寫入 docs/phases/p2-results.zh-HK.md。

授權：可以 commit 到 phase/p2-long-replay（唔可以 push）。可以用 npm run test:note-models-live 跑：完整 Week 3 transcript Qwen batch+organize 一次、廣東話 fixture Qwen 一次、Gemini 3.5 Flash Lite 對照各一次；失敗最多重跑一次。合計預算上限 US$3，超出前停低問我。
```

### P3：筆記結構（organizer 範圍、subtopic 標題）

```text
你負責 Playback 嘅 phase P3。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p3-note-structure。

目標：
A. Organizer 經常想改佢範圍外嘅主題，一次有 7–8 個操作被丟棄（見 docs/note-model-comparison-2026-10-18.md 同 app-temp/data/validation/runs/2026-10-18-qwen-organize-v13）。由保存咗嘅 run 搵出被丟棄操作嘅模式，再決定修法：例如同一層太多主題時俾佢成個 outline、範圍包埋上層，或者改 schema 令範圍外主題根本揀唔到。揀一個並喺結果文件寫原因。
B. 主題已滿（NoteStructure：六段或八分鐘）時自動開嘅 subtopic，標題仍似一句句子，要等 organizer 改名。令新 subtopic 一開始就有名詞短語標題（例如由 writer 喺 outline 宣告，或者由同一次 call 提供），唔加多一次 model call。

擁有：app-temp/application/Notes/NoteWorkflow.Organization.cs、NoteWorkflow.Edits.cs、NoteInstructions.cs、NoteInput.cs、app-temp/domain/Notes/NoteStructure.cs、NoteOrganization.cs、NoteTree.cs、app-temp/checks/Offline/NoteOrganizationChecks.cs、NoteEditChecks.cs、app-temp/checks/Fixtures/**。

步驟：先用保存嘅 run 重現兩個問題，寫離線 check 令佢 red，改 code／prompt 令佢 green，升 prompt 版本。更新 STRUCTURE.md 入面 Note placement／Note edits／Note admission and organization 幾行（呢幾行屬你）。

完成條件：兩個問題各有一個以前 red、而家 green 嘅離線 check；全部離線 checks 綠；結果文件寫低一個 live 驗收計劃（用 P2 嘅 runner、Week 3 頭 20 分鐘、Qwen、預計費用），量度被丟棄操作數同 subtopic 標題。

授權：可以 commit 到 phase/p3-note-structure（唔可以 push）。可以用 Week 3 頭 20 分鐘跑一次 Qwen batch+organize live 驗收（約 US$0.05），失敗最多重跑一次。
```

### P4：筆記 provider（streaming、fallback、OpenCC 詞組）

```text
你負責 Playback 嘅 phase P4。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p4-note-provider。

目標：
A. Streaming：Qwen 每次 call 約 10 秒，而 OpenRouter writer 唔係 streaming（NoteProviderAdapters.cs 第 43 行），所以 Activity 嘅 live draft 要等成個回覆先出現。用 OpenRouter 官方 streaming（SSE）接入 onUpdate，令 draft 邊收邊顯示；structured output 同 streaming 一齊用嘅行為、usage 喺邊個 event 出現、取消同 timeout，都要先查 OpenRouter 官方文件。保留 response-size 上限同固定 endpoint。
B. Fallback：OpenRouter 失敗時而家唔會改用 Gemini。開工時先問我：(1) 唔 fallback，照實顯示失敗；(2) 可重試嘅錯誤（timeout、5xx、429）用完重試後改用 Gemini，Activity 標明實際 writer；(3) 其他。我建議 (2)。按我答案實作，fallback 要喺 Activity 同保存紀錄睇得到。
C. OpenCC 詞組：「示范」呢類全部由共用字組成嘅簡體詞未轉。試 OpenCC 嘅詞組轉換，但保留香港用法（例如「了解」、「干擾」、「公里」、「游泳」、「着」）。用 Week 3 run 嘅真實輸出整一份正反例 fixture。

擁有：app-temp/infrastructure/Providers/OpenRouterTextModel.cs、ModelOutputContract.cs、app-temp/infrastructure/Notes/**、app-temp/checks/Offline/OpenRouterTextChecks.cs，同你新增嘅 check 檔案。只喺 Activity 要顯示新欄位時先改 application/Activity（列入範圍外改動）。

完成條件：A、B、C 各有離線 check（in-memory HTTP 模擬 SSE、斷線、重試、fallback）；STRUCTURE.md Notes 嗰行同 Note script 嗰行更新（呢兩行屬你）；結果文件附 live 驗收計劃。

授權：可以 commit 到 phase/p4-note-provider（唔可以 push）。可以做最多 5 次細嘅 live OpenRouter streaming call（短 fixture，唔上傳音訊）驗證 SSE 格式同 usage。
```

### P5：DB／儲存遷移計劃（只寫文件）

```text
你負責 Playback 嘅 phase P5。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p5-storage-plan。

目標：為 docs/next-session-checklist.zh-HK.md「未來想做」第 6 項（Neon PostgreSQL + EF Core、Cloudflare R2、Cloud Run asia-southeast1）寫一份可以直接照住做嘅分階段計劃。方向已經決定，唔使重新比較其他 DB。

只寫文件：docs/storage-migration-plan.zh-HK.md 同 docs/phases/p5-*。唔改 code，唔裝 package，唔郁任何 database。

內容：
1. 盤點：app-temp/infrastructure/Storage/** 每個 Mongo collection 嘅用途、讀寫路徑、index、CAS／idempotency 規則（例如 note_heads expected-version CAS、pending-history marker、chunk identity session+source+sequence+hash），以及對應 PostgreSQL table／jsonb 設計同點樣保住每條規則。
2. 階段：1 users／sessions／groups → 2 transcripts／chunks／audio（R2 object key audio/<session>/<source>/<seq>-<hash>.wav、signed URL 播放、本機用檔案或 MinIO）→ 3 notes／versions／terms／chat／activity。每階段：改動檔案、repository port 點樣保持唔變、migration、checks（LocalMongo/ 對應嘅 Postgres 版本）、rollback、完成條件。
3. 一齊處理：登入 signing keys 放 DB；AI activity 唔再存完整 prompt（每次最多 20 萬字）而改存 hash／摘要或者放 R2，估計 Neon 0.5 GB 免費額度可以用幾耐；Cloud Run 背景工作要 1 個長開 instance 嘅成本同改用 Cloud Tasks 等事件觸發嘅選項。
4. 本機開發：本機 Postgres 用 docker compose 定直接連 Neon dev branch，列利弊同建議。
5. 清本機 MongoDB 前要問嘅嘢：目標 DB、會失去嘅資料、重建計劃（AGENTS.md 要求），寫成一段可以直接俾我批准嘅文字。

每個 EF Core／Npgsql／Neon／R2／Cloud Run 嘅做法都附官方文件連結，標明查閱日期。

完成條件：每個 collection 都有對應去向；每階段有可檢查嘅完成條件；所有外部做法有官方來源。

授權：可以 commit 到 phase/p5-storage-plan（唔可以 push）。可以上網讀官方文件。
```

### P6：研究講者分離同文件抽取（只寫文件）

```text
你負責 Playback 嘅 phase P6。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p6-research。

只寫文件：docs/research/diarization.zh-HK.md、docs/research/document-extraction.zh-HK.md、docs/phases/p6-*。唔改 code，唔裝 package，唔上傳音訊。

A. 講者分離：而家只分 microphone 同 system audio 兩個來源。比較支援 diarization 嘅 ASR provider（OpenRouter 上嘅模型同直接 provider）同獨立 diarization 模型（例如可以喺 .NET／本機跑嘅選項）：廣東話同中英夾雜、會議同課堂、即時定只限事後、講者數上限、timestamps 格式、點樣同現有 chunk（session+source+sequence+hash）同 retry 對得上、價錢、私隱（音訊去邊）。建議一個方案同一個後備，寫低 live 驗收計劃（用 Week 3 sample 邊一段、請求數、費用）。
B. 文件抽取：materials 而家只收純文字（POST /api/sessions/{id}/materials，name + text）。比較 .NET 10 抽取 PDF、PPTX、DOCX 文字同頁碼／slide 號嘅 library：license、維護狀態、掃描 PDF 要唔要 OCR、表格同數學、記憶體上限同惡意檔案防護（zip bomb、超大頁數）。建議一個方案，同埋引用點樣指返原文件位置嘅資料格式。

每個 provider／library 嘅說法都附官方文件或者 repo 連結，標明查閱日期同版本。

完成條件：每個候選都有同一組欄位嘅比較表；每項建議有來源；有 P8 同 P13 可以直接用嘅結論段。

授權：可以 commit 到 phase/p6-research（唔可以 push）。可以上網讀官方文件同 repo。
```

---

## Wave 2（Wave 1 合併入 main 之後）

### P7：Prompt profile 同會議模式

```text
你負責 Playback 嘅 phase P7。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p7-profiles。先讀 docs/phases/p2-results.zh-HK.md、p3-results.zh-HK.md。

目標：按 session／group 揀筆記 profile：「課堂」（而家嘅行為）同「會議」（議程、決定、行動項目：負責人、期限、來源）。兩個 profile 重用同一個位置決定 harness、NoteStructure 結構規則同 organizer；分別只係 prompt 文字、outline 嘅建議頂層同 block 種類。觸發條件（30 秒批次、停頓、Stop）抽成 profile 可以設定嘅事件規則，為之後「累積字數、換講者」等事件留位置，但今次只實作現有三種。

擁有：app-temp/application/Notes/NoteInstructions.cs、新增 profile 檔案、ProcessingSettings 相關 application／api／web 檔案（ProcessingSettingsDialog.tsx 等）、對應 checks。

完成條件：揀會議 profile 嘅 session，離線 fixture 出到議程／決定／行動項目結構，課堂 profile 輸出同改之前一樣（用現有 checks 證明）；設定保存、重載後保留；prompt 版本升級；STRUCTURE.md 有關幾行更新。結果文件附 live 驗收計劃。

授權：可以 commit 到 phase/p7-profiles（唔可以 push）。
```

### P8：上載 PDF／PPT／Word 做 materials

```text
你負責 Playback 嘅 phase P8。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p8-documents。先讀 docs/research/document-extraction.zh-HK.md（P6）。

目標：每個 session 可以上載 PDF、PPTX、DOCX，抽出文字同頁碼／slide 號存成 materials；筆記同 Ask 引用 material 時顯示文件名同頁碼，撳落去睇到嗰頁抽出嘅文字。限制檔案大小同類型，惡意或者壞檔案顯示清楚錯誤。今次只保存抽出嘅文字同位置資料；原檔儲存等 P11 嘅 object store。

擁有：app-temp/api/Sources/MaterialSources.cs、app-temp/application/Notes/MaterialSources.cs、新增抽取 adapter（infrastructure）、app-temp/web/src/features/materials/**、引用顯示需要嘅 features/sources 改動、對應 checks。經現有 repository port 存資料；P9 同時改緊 Storage，所以唔好改 infrastructure/Storage，需要新欄位就列入範圍外改動。

完成條件：每種格式有細嘅 fixture 檔案同離線 check（正常、超大、壞檔、加密 PDF）；browser check 覆蓋上載同引用頁碼；STRUCTURE.md materials 行更新。

授權：可以 commit 到 phase/p8-documents（唔可以 push）。可以用 dotnet add package 安裝 P6 建議嘅抽取 library（固定版本）。
```

### P9：DB 遷移第 1 階段（users／sessions）

```text
你負責 Playback 嘅 phase P9。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p9-postgres-stage1。按 docs/storage-migration-plan.zh-HK.md（P5）第 1 階段做。

第一步：照計劃第 5 節，列出目標 DB、會失去嘅資料同重建計劃，等我明確批准先郁任何 database。批准之前只可以做唔郁 DB 嘅 code。

擁有：計劃第 1 階段列出嘅檔案（infrastructure/Storage 入面 users／sessions／groups 部分、新 EF Core DbContext 同 migration、ServiceRegistration 嘅 storage 註冊）、對應 checks。

完成條件：計劃第 1 階段嘅完成條件全部達到；離線 checks 綠；Postgres 版本嘅 store checks 綠；其他 collection 仍然用 Mongo 照常 work；Neon connection string 只由 process environment 讀。

授權：可以 commit 到 phase/p9-postgres-stage1（唔可以 push）。可以安裝計劃列出嘅 NuGet package（固定版本）。可以啟動本機 Postgres container（如果計劃揀咗 docker）。清或者重建任何 database 仍然要我另外批准。
```

### P10：FAQ mode

```text
你負責 Playback 嘅 phase P10。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p10-faq。

目標：喺一個 session 入面，Playback 問我幾條關於課堂重點嘅問題（我識／唔識／答一句），再根據課堂內容同我嘅答案，預測我可能唔明嘅概念，整理成 FAQ：每條有問題、解答同 transcript／material 來源引用。可以重新生成，保存並有歷史。重用 Ask Playback（application/Ask/ChatAgent.cs，有引用）同術語功能；新 prompt 有版本號。先對照 docs/business-requirements.md 同 docs/scope-decision.zh-HK.md 定範圍，有衝突先問我。

擁有：新增 application/Ask/Faq*、api FAQ endpoint、web/src/features/faq/**、PlaybackPage.tsx 入面加入口嘅最少改動、對應 checks。唔改 ChatAgent 嘅現有行為。

完成條件：離線 fixture model 走完「問問題 → 收答案 → 生成 FAQ → 引用跳返 transcript」，有 application check 同 browser check；FAQ 每條都有有效引用；STRUCTURE.md 加 faq 一行。結果文件附 live 驗收計劃。

授權：可以 commit 到 phase/p10-faq（唔可以 push）。
```

---

## Wave 3（Wave 2 合併之後）

### P11：DB 遷移第 2–3 階段

```text
你負責 Playback 嘅 phase P11。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p11-postgres-stage2-3。按 docs/storage-migration-plan.zh-HK.md 第 2、3 階段做，先完成第 2 階段（transcripts／chunks／audio 去 R2）再開始第 3 階段（notes 等），兩個階段各自一個 commit。郁任何 database 前照計劃第 5 節列出目標、損失同重建計劃，等我批准。完成條件：計劃第 2、3 階段嘅完成條件；chunk identity 同 retry 冪等有 check；R2 key 只由 process environment 讀。授權：可以 commit 到 phase/p11-postgres-stage2-3（唔可以 push）；可以安裝計劃列出嘅 package（固定版本）。
```

### P12：Login

```text
你負責 Playback 嘅 phase P12。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p12-login。以真正帳戶取代 api/Security/DemoAccess.cs 嘅共用 demo 密碼：開工時先問我用密碼（ASP.NET Core Identity 嘅 hashing）定 OAuth（邊個 provider）。Session 擁有權同權限沿用現有 ownership middleware；signing keys 放 DB。完成條件：註冊／登入／登出／他人 session 被拒，各有 check；現有 CSRF／same-origin checks 仍然綠；docs/cloud-run-deployment.md 更新。授權：可以 commit 到 phase/p12-login（唔可以 push）。
```

### P13：講者分離實作

```text
你負責 Playback 嘅 phase P13。先讀 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md 嘅「共同規則」，然後照規則做。Branch：phase/p13-diarization。按 docs/research/diarization.zh-HK.md 嘅建議方案實作：逐字稿每段有講者，使用者可以為講者命名，筆記（特別係會議 profile）同 transcript 顯示講者名。保留 microphone／system audio 來源分別。完成條件：離線 check 覆蓋講者標記、改名、retry 冪等；browser check 覆蓋顯示同改名；結果文件附 live 驗收數據。授權：可以 commit 到 phase/p13-diarization（唔可以 push）；可以按研究文件嘅 live 驗收計劃上傳 Week 3 sample 指定嗰段一次。
```

---

## 收尾 M：每個 wave 合併之後

```text
Phase branch 已經合併入 main。讀 docs/phases/*-results.zh-HK.md 同 C:\Users\a1831\Documents\GitHub\playback\docs\parallel-phases.zh-HK.md，然後：更新 docs/next-session-checklist.zh-HK.md（剔已完成、加結果文件連結、加各 phase 列出嘅未完成項）、app-temp/README.zh-HK.md 嘅現況同限制、核對 STRUCTURE.md 同 code 一致。處理各結果文件「範圍外改動」一節列出嘅衝突。跑全部離線 checks 同 web build，失敗照實報告。唔好 commit，等我睇過。
```
