# P3 結果：筆記結構（organizer 範圍、subtopic 標題）

Branch：`phase/p3-note-structure`（由 `main` 3cecbe0 開）。Prompt：writer `tree-notes-v13` → `tree-notes-v14`；organizer `tree-organize-v13` → `v14`（第一次 live）→ `v15`（live 之後再修）。

## 摘要

- **A（organizer 丟棄操作）**：保存咗嘅 run 入面，被丟棄嘅操作**冇一個係改範圍外主題**。每個被丟棄嘅搬動都係搬範圍內嘅 block 或主題，只係冇講搬去邊：`move-block` 嘅 `topicId` 冇填、填 `""`，或者填返 block 而家所在嘅主題。修法：organizer 改用「放置」，`moves: [{block, to}]` 同 `topicMoves: [{topic, to}]`，同 writer 嘅 `blocks[].topic` 一樣，`to` 只解作目的地，而且 required。Schema 只容許 `to` 揀範圍內主題或者新 key，所以冇得漏、冇得揀範圍外。仍然套用唔到嘅操作按原因分開計。
- **B（自動 subtopic 標題）**：句子標題只會喺 writer 嘅回覆指向已滿主題時出現，application 用 block 第一句做標題。修法：application 唔再自己起標題。Block 放入同一個回覆喺 outline 宣告嘅 subtopic（writer 起嘅名詞短語標題）；冇宣告就留喺原主題，超過六段時由 organizer 拆。冇加 model call。
- **另外修咗**：(1) 新增或者搬入嘅 block 以前用回覆入面嘅位置做 `Order`，會排喺之前嘅 block 中間；而家排喺主題最後。(2) 只有 outline 嘅重組（同一層太多主題）以前遇到 `rename-topic` 就成次失敗；而家可以改名。
- 離線 checks 全綠（27 組）。兩次 live 驗收（US$0.056）：拆大主題 7 個 move 全部套用、0 丟棄、0 句子標題；但重組太擠嘅一層 7 次都冇效，因為 Qwen 冇填 `move-topic` 嘅 `parentId`。呢個已經用 `topicMoves`（`tree-organize-v15`）修好，有離線 check，**但未 live 驗證**（授權嘅重跑次數已用完）。

## A. Organizer 被丟棄嘅操作

### 由保存 run 搵到嘅模式

逐個對比兩個 Qwen3.8 Flash batch+organize run（Week 3 頭 20 分鐘，`app-temp/data/validation/runs/`）嘅 organizer 回覆同 activity summary：

| Run | Organizer call | 被丟棄 | 實際原因 |
| --- | --- | --- | --- |
| `2026-10-18-qwen-organize-v13`（`topicId` 未 required） | #1（拆 `AI輔助學習策略…`） | 8 | 7 個 `move-block` 冇 `topicId`，目的地只寫喺 `reason`（「歸入『AI輔助學習價值』主題」）；另一個係重複嘅 placeholder move（`"..."`）|
| 同上 | #3（拆 `系統呼叫鏈與 NtQueryInformationFile(?)`） | 7 | 7 個 `move-block` 冇 `topicId`；`reason` 寫住「移動至新主題『調試器操作與符號解析』」|
| `2026-10-18-placement-v13b`（`topicId` 已 required） | #1、#3 | call 失敗 | 冇標題嘅 rename（後來已改為丟棄）；而 `move-block` 全部填咗 block **而家所在**嘅主題（7 個、3 個）|
| 同上 | #2 | 1 | `move-block` 填 `topicId: ""`（下一個操作再用 `new1` 重做）|
| 同上 | #4 | 1 + 3 個無效 | `move-topic` 用 `parentId: ""` 表示頂層；3 個 `move-block` 填返原主題，搬咗等於冇搬，新 subtopic 因為空咗而被刪 |

結論：16 個被記為「範圍外」嘅操作冇一個指向範圍外嘅主題或 block。問題係 `move-block` 嘅 `topicId`。其他操作（`rename-topic`、`move-topic`、`reorder-topic`）嘅 `topicId` 係「被操作嘅主題」，只有 `move-block` 嘅 `topicId` 係「目的地」。Qwen 唔係漏咗，就係當咗佢係 block 而家所在嘅主題。

### 考慮過嘅修法

| 修法 | 會唔會解決 | 原因 |
| --- | --- | --- |
| 同一層太多主題時俾成個 outline | 唔會 | 丟棄全部發生喺拆大主題（`large-topic`），唔係重組太擠嘅一層 |
| 範圍包埋上層 | 唔會 | 冇一個操作指向上層 |
| **改 schema，令範圍外主題揀唔到**（已揀） | 會 | 用只解作目的地嘅 required 欄位：`moves: [{"block", "to"}]` 同 `topicMoves: [{"topic", "to"}]`，同 writer 已經做得好嘅 `blocks[].topic` 同一個形狀。`to` 嘅 enum 只有範圍內主題同 `new1`–`new8`，冇 `""`，所以目的地漏唔到、亦揀唔到範圍外。Organizer 嘅 operation 只剩 `rename-topic`、`reorder-topic`、`reorder-block`（只有 outline 時冇 `reorder-block`），搬動只有一個方法 |

`topicMoves` 係第一次 live 之後先加（見[下面](#live-驗收)）。`tree-organize-v14` 原本只係俾 `move-topic` 嘅 `parentId` 加 enum，但冇設 required。

仲做咗：

- 冇目的地、目的地係自己（`parentId` 等於 `topicId`），或者搬完都喺原位（block 搬去原本主題、主題搬去原本 parent）嘅 move 照丟，但同「範圍外」分開計。Activity summary 分開寫 `operations outside the selected scope dropped`、`moves without a destination dropped`、`moves that leave a block or topic where it is ignored`。
- 只有 outline 嘅重組可以 `rename-topic`（佢有標題，改名係結構操作）。以前一有 rename 就成次失敗。
- Organizer prompt 講明 `moves`／`topicMoves`，附拆主題同重組各一個例子。

### Checks

`NoteOrganizationChecks.SavedRunMoves`（fixture `checks/Fixtures/SavedOrganizerReplies.cs`，由 qwen-organize-v13 第 3 次 organizer call 抽出：主題、7 個 block ID、原本回覆）：

1. 原本回覆經真實 organizer 流程（`NoteAgent` tick → large-topic）：而家報告「7 moves without a destination dropped」，唔再話係範圍外。
2. 同一組決定用 `moves` 形狀（由原本 `reason` 寫返出嚟，**唔係** provider 回覆）：5 個 block 按回覆列出嘅次序搬入兩個新 subtopic，原主題剩 2 段，冇丟棄。
3. 用 organizer 真正收到嘅 input 砌 schema：`moves.items.to` 嘅 enum 正好係範圍內主題加 `new1`–`new8`，`block` 嘅 enum 係嗰 7 個 block，兩者都 required，operation 種類冇 `move-block`。

改之前（red）：

```
CheckFailed: The saved reply's moves were blamed on the scope instead of their missing destination: Section patch saved; untouched sections retained; 0 sources have written points, 0 deferred, 0 remain pending without a model disposition; 7 operations outside the selected scope dropped
```

`NoteOrganizationChecks.SavedRunRegroup`（fixture 由第一次 v14 live run 嘅重組回覆抽出）：7 個頂層主題，回覆宣告兩個 parent，但 `move-topic` 冇 `parentId`。而家報告「7 moves that leave a block or topic where it is ignored」。用 `topicMoves` 加一個 rename 嘅版本會將 7 個主題分入兩個 parent，並且改到名。Schema 方面，`topicMoves` 嘅 `topic`／`to` 都 required，enum 正確；operation 種類只有 `rename-topic`、`reorder-topic`。

改之前（red）：

```
CheckFailed: A regroup whose topic moves named no parent was not reported: Section patch saved; untouched sections retained; 0 sources have written points, 0 deferred, 0 remain pending without a model disposition
```

## B. 主題已滿時自動開嘅 subtopic

### 重現

- 句子標題只喺 `RedirectFromClosedTopics` 出現：回覆將 block 放入已滿（六段）或者講咗八分鐘嘅主題，application 就開一個 subtopic，用 block 嘅第一句做標題（最多 24 字加「…」）。
- 保存嘅 run：`2026-10-18-benchmark-final` Gemini 3.5 Flash-Lite（`tree-notes-v11`）有 9 個 block 咁樣被轉走。最後 8 個主題入面 4 個係句子標題，例如「使用 AI 複習講課內容是一種非常好的方式」、「講師提及自己透過 AI 重新整頓了整個 Wind…」、「講師說明需要輸入的第一個指令，是用來確保視窗偵錯…」。
- `tree-notes-v13` 嘅 schema 已經唔俾 `blocks[].topic` 揀已滿主題，所以三個 v13 run（約 90 次 writer call）同今次兩次 live 都冇觸發。但 schema 唔係每次都守得住（舊 `operations` 形狀嘅 `add-block`、唔跟 schema 嘅 provider），一觸發就會出句子標題。

### 修法

Application 唔再自己起標題：

1. 回覆指向已滿主題嘅 block，放入**同一個回覆**喺 outline 為佢宣告嘅 subtopic。Parent 係嗰個已滿主題；如果已滿主題已經喺第三層，就放喺佢旁邊（同一個 parent 下面）。標題由 writer 起，係名詞短語。
2. 冇宣告就留喺原主題。主題超過六段，organizer 會好似其他大主題咁拆，標題由 organizer 起。冇 source 被拒，冇加 model call。
3. Writer prompt（`tree-notes-v14`）明確講：要延續已滿主題，就喺 outline 宣告佢下面（第三層就係旁邊）一個 subtopic，標題講新嘅方面，再將 block 放入嗰個 key。

Activity summary 分開寫 `blocks for closed topics placed in the subtopic the reply declared` 同 `blocks for closed topics kept there without a declared subtopic`。

### Check

`NoteEditChecks.ClosedTopics`（改寫）：六段嘅主題「Scheduling」已滿。回覆宣告 `new1`「輪轉排程」（parent 係 Scheduling），但將 block 放咗入 Scheduling → block 入咗「輪轉排程」。第二個回覆冇宣告 → block 留喺 Scheduling（7 段），冇新主題。之後 writer 自己喺已滿主題下面開 subtopic，照常得。

改之前（red）：

```
CheckFailed: A block aimed at a full topic did not go into the subtopic the same reply declared: Scheduling | 輪轉排程使用時間片
```

## Block 次序

`NoteTree` 以前直接將回覆入面嘅位置（`blocks` 嘅 index、`move-block` 嘅 `order`）當 block 嘅 `Order`，而 Markdown 同畫面都係按 `Order` 排。每個回覆由 0 開始，所以後來嘅 block 會排喺之前嘅 block 中間。保存咗嘅 qwen-organize-v13 筆記入面，`系統呼叫鏈與 NtQueryInformationFile(?)` 嘅 7 段 order 係 1,0,0,1,0,0,1。而家新增同搬入嘅 block 排喺該主題最後一段之後（`reorder-block` 照用指定位置）。

Check：`NoteEditChecks.Placement` 加咗兩個回覆，加 block 去同一個主題，要求閱讀次序同講嘅次序一樣。改之前（red）：`Blocks of a later reply were shown between earlier blocks: 缺頁先 | 被改過 | 缺頁時 | 沒有空`。

## 證據（離線）

```powershell
dotnet restore app-temp/checks/Playback.Checks.csproj --configfile app-temp/checks/NuGet.Offline.Config
dotnet build app-temp/api/Playback.Api.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
#   0 Warning(s) 0 Error(s)
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
#   exit 0；27 組 "passed"，包括：
#   Organization checks passed: ... block and topic moves name their destination (saved Qwen split and regroup replayed), ...
#   Note edit checks passed: ... closed topics (full or long-running) continue in the subtopic the reply declared, never one titled by a sentence, ...
```

開工前（3cecbe0）同一組指令都係 27 組綠。今次冇改 web，所以冇跑 web build 同 browser checks。

## Live 驗收

指令（喺 worktree；`.env` 由 main checkout 載入 process，冇顯示內容）：

```powershell
node --env-file-if-exists=<main checkout>/.env app-temp/checks/Validation/run-note-models.mjs --live `
  --models "openrouter:qwen/qwen3.8-flash@none|batch+organize" --folder app-temp/data/validation/runs/<folder>
```

量度方法：用 `all-activity.json` 嘅 organizer summary 按原因數被丟棄操作；用 `replies.json` 數 `moves`／operation；用 `note.json` 睇最後主題標題（句子標題 = 有「。！？，」或者「講師／說明／提及／指出…」，再人手睇）。

| | Run 1 `2026-10-19-p3-organize-v14` | Run 2（重跑）`2026-10-19-p3-organize-v14b` | 參考：`2026-10-18-qwen-organize-v13` |
| --- | --- | --- | --- |
| Prompt | notes v14 / organize v14 | notes v14 / organize v14 | v13 / v13 |
| Writer calls（failed） | 29（1） | 29（0） | 30（0） |
| 來源引用 | 58/58 | 58/58 | 58/58 |
| Median call | 9.4 s | 8.0 s | 10.1 s |
| 費用 | $0.0276 | $0.0286 | $0.0350 |
| Organizer calls（failed） | 0 | 8（2） | 3（0） |
| 拆大主題：搬動／套用／丟棄 | — | 7 / 7 / 0 | 15 / 0 / 15 |
| 重組太擠一層：有效果 | — | 0 / 7 | 1 / 1 |
| 「範圍外」丟棄 | 0 | 0 | 15 |
| 已滿主題轉走（宣告／留低） | 0 / 0 | 0 / 0 | 0 |
| 最後主題（頂層） | 19（5） | 15（11） | 12（2） |
| 句子標題 | 0 | 0 | 0 |

- **Run 1** 冇觸發 organizer：writer 自己喺現有主題下面宣告咗 12 個 subtopic，冇主題喺第三層以上超過六段，亦冇一層超過六個主題。所以 A 量度唔到，用咗授權嘅一次重跑。唯一失敗嘅 writer call 係 Qwen 喺 block 內文寫咗自創嘅 `[cite_T46,T47,T48]`，被引用檢查拒絕；同 P3 改動無關。
- **Run 2** 拆大主題：Qwen 用咗 `moves`，7 個 block 全部搬入兩個新 subtopic，0 丟棄（v13 同類 call：7/7 丟棄）。
- **Run 2** 重組太擠一層（7 次）：每個 `move-topic` 都**冇 `parentId`**。主題「搬」去頂層（本身已經喺頂層），新 parent 空咗被刪；5 次 completed 但冇效果，2 次因為 outline 重組唔准 `rename-topic` 而失敗。v13 兩次同類 call 都有填 `parentId`，所以應該係 v14 俾 `parentId` 加 enum 但唔係 required 引起。已經用 `topicMoves`（required `to`）同准許改名修好（`tree-organize-v15`），有離線 check，**未 live 驗證**。
- 兩次 live 都冇句子標題、冇已滿主題被轉走：writer 跟 schema，自己宣告 subtopic。
- 兩次報告都有 2 個簡體字（runner 量度未經 OpenCC 嘅模型原始輸出），屬 P2／P4。

合計 live 費用 US$0.0562（授權約 US$0.05，一次重跑）。Run folder 喺 worktree 嘅 `app-temp/data/validation/runs/`（git ignore），合併 worktree 前如果要保留，要抄去 main checkout。

### 下一次 live 驗收計劃（未授權，未做）

- 目的：驗證 `tree-organize-v15` 嘅 `topicMoves` 喺重組太擠一層時有效，同埋 `moves` 再次 0 丟棄。
- 指令：同上，`--folder app-temp/data/validation/runs/<日期>-p3-organize-v15`，Qwen3.8 Flash batch+organize，Week 3 頭 20 分鐘；如果 P2 嘅 runner 已經合併，用佢嘅 OpenCC 選項量度最終筆記。
- 請求數：約 29 次 writer call、0–8 次 organizer call（視乎結構）；冇上傳音訊。
- 預計費用：每次約 US$0.03（兩次 v14 run：$0.0276、$0.0286）；建議授權最多兩次（organizer 唔一定觸發），上限 US$0.07。
- 量度：organizer 各原因丟棄數（目標：範圍外 0、冇目的地 0、原位 ≤ 2）；重組後頂層主題數（目標 ≤ 6）；`topicMoves` 套用數；句子標題數（目標 0）；已滿主題轉走（宣告／留低）。

## 未完成同限制

- `tree-organize-v15`（`topicMoves`、outline 重組可改名）只有離線 check，未 live。
- Fixture 入面嘅 `PlacedReply`、`PlacedRegroupReply` 係按保存回覆寫返出嚟嘅新形狀，唔係 provider 真正嘅回覆；`moves` 部分已經喺 Run 2 由 Qwen 真正用過。
- Organizer 仍然可以將 block 或主題「搬」去原位（schema 冇辦法逐個排除）。呢種 move 唔會改到嘢，會分開計，prompt 亦講明唔好咁做。
- B 嘅後備（冇宣告就留喺原主題）會令已滿主題暫時超過六段，要等 organizer 拆；organizer 停用時會一直咁。
- Organizer 唔再可以將主題搬去頂層（`to` 冇 `""`）。拆主題同重組都唔需要；writer 嘅 `move-topic` 用 `parentId: ""` 仍然解作頂層。
- 保存嘅 run（`app-temp/data/validation/runs/`）冇 commit；fixture 只抄咗需要嘅 ID、標題同回覆結構。Organizer input 冇保存喺 activity（`promptText` 只有指示），所以 fixture 主題係由 `note.json` 同回覆砌返出嚟，block 內文縮短咗。

## 範圍外改動

| 檔案 | 改咗咩 | 原因 |
| --- | --- | --- |
| `app-temp/infrastructure/Providers/ModelOutputContract.cs`（P4 擁有） | `Regrouping` 加 `moves`、`topicMoves`（enum、required；冇 block／主題時 `maxItems: 0`），organizer operation 種類改為 rename／reorder（outline 冇 `reorder-block`），`required` 加兩個欄位；`StructuralKinds` 拎走 `move-topic`、`move-block`；`Placement` 拎走 `moves`、`topicMoves`（writer 同 owner instruction 冇呢兩個欄位） | A 嘅修法本身就係 schema，呢個檔案係唯一砌 schema 嘅地方。改動只喺 `ResponseFormat` 傳 blockIds 嗰一行、`Regrouping`、`Placement` 尾同 `StructuralKinds`，同 P4 嘅 streaming／fallback 應該唔重疊 |
| `app-temp/domain/Notes/NoteSections.cs` | `NotePatch` 加 `Moves`、`TopicMoves` 兩行同註解 | 回覆嘅 CLR 型別喺呢度；`BlockMove`／`TopicMove` class 放咗喺 `NoteTree.cs`（屬 P3） |
| `app-temp/application/Notes/NoteWorkflow.cs` | `DropOutOfScope` 回傳分類計數；summary 文字（`Dropped`、`EditSummary` 嘅已滿主題兩句）；outline 重組准許 `rename-topic` | 按原因計丟棄、B 嘅新 summary，同埋 Run 2 嘅 rename 失敗都喺呢度 |

## 要使用者決定嘅嘢

- 授權一次 `tree-organize-v15` live 驗收（見上面計劃，約 US$0.03–0.07）。
- B 嘅後備：回覆冇宣告 subtopic 時，我揀咗「留喺原主題，等 organizer 拆」，唔再自動開 subtopic。另一個做法係拒收嗰段（source 留 pending，下一批再寫），但可能多一次 writer call。建議維持現狀。
- Run folder 要唔要由 worktree 抄去 main checkout 保留。
