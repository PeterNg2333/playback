# P4 結果：筆記 provider（streaming、fallback、OpenCC 詞組）

Branch：`phase/p4-note-provider`（由 `main` 3cecbe0 開）。日期：2026-10-19。

## 摘要

| 目標 | 結果 | 離線 check |
| --- | --- | --- |
| A. OpenRouter streaming | OpenRouter 筆記 writer 改用 SSE，Activity 嘅 live draft 邊收邊顯示；live 測試第一個 draft 喺 2.9 秒出現，成個回覆 7.8 秒 | `OpenRouterTextChecks`（SSE、斷線、靜默、deadline、取消、大小上限）、`NoteWriterFallbackChecks.LiveDraftWhileStreaming`（經真 `NoteWorkflow`，回覆未完 Activity 已見 draft） |
| B. Fallback（已答：選項 2） | 可重試錯誤重試兩次（1 秒、4 秒，或者 provider 嘅 Retry-After，上限 20 秒），三次都失敗或者超過 60 秒窗口就交俾 Gemini 寫；Activity 即時同保存紀錄都顯示實際 writer 同原因 | `NoteWriterFallbackChecks`（重試、Retry-After、窗口、fallback、唔重試嘅錯誤、取消、workflow 保存紀錄） |
| C. OpenCC 詞組 | 全部由共用字組成嘅簡體詞（示范、根据、由于、背后、制作、精准）而家會轉；香港用法（了解、公布、宣布、台下、上周、干擾、公里、游泳、着）保留。Week 3 run 4,218 段真實輸出：正確繁體零誤改 | `NoteScriptChecks`（10 個轉換例、14 個保留例，大部分來自 Week 3 run） |

`dotnet build` 同全部離線 checks（29 組）通過。冇改 web，所以冇跑 web build／browser checks。冇改 prompt 文字，prompt 版本唔使升。

## A. Streaming

### 查證咗嘅 OpenRouter 官方文件（2026-10-19 讀）

- [Streaming](https://openrouter.ai/docs/api-reference/streaming)：SSE `data:` 行；`: OPENROUTER PROCESSING` 係 comment，要略過；`data: [DONE]` 結束。Usage 喺 `[DONE]` 之前最後一個 chunk，個 chunk 有一個 `delta` 冇內容嘅 choice，並且重複 `finish_reason`（OpenRouter 刻意同 OpenAI 唔同）。取消就係斷開連線；**Alibaba 唔支援取消**，即係 Qwen3.8 Flash 斷線之後都照收成個回覆嘅錢。
- [Errors](https://openrouter.ai/docs/api-reference/errors)：開始之前嘅錯誤係非 200 JSON（400/401/402/403/408/429/502/503）；429、503 可能有 `Retry-After`（秒）。開始之後嘅錯誤係 HTTP 200 入面一個有頂層 `error`、`finish_reason: "error"` 嘅 SSE event，之後 stream 結束；500 類錯誤訊息會被換成通用字串。非 streaming 嘅 200 回覆都可能有 `error`。
- [Structured outputs](https://openrouter.ai/docs/features/structured-outputs)：`stream: true` 同 `response_format`（json_schema、strict）可以一齊用，會串流「完成後符合 schema 嘅部分 JSON」。[Response healing](https://openrouter.ai/docs/features/response-healing) 只適用於非 streaming（我哋本身冇開）。
- [Usage accounting](https://openrouter.ai/docs/use-cases/usage-accounting)：streaming 嘅 usage 一定喺最後一個 chunk；`usage: {include: true}` 已 deprecated、冇效果，所以 payload 刪咗佢。

### 改動

- `infrastructure/Providers/OpenRouterTextModel.cs`：有 `onUpdate` 就加 `stream: true`，逐行讀 SSE（多行 `data:` 合併、comment 略過、`[DONE]` 結束），每個 delta 累積成回覆，draft 最多每 250 ms 更新一次（第一次即刻）。Usage（連 serving provider、cost、cached tokens）由有 `usage` 嘅 chunk 報。固定 endpoint、`AllowAutoRedirect = false`、2,000,000 bytes 上限（而家連 SSE 原始 bytes 一齊計）保留。回覆係 JSON 而唔係 `text/event-stream` 就照舊讀。
- 時限：成個 call 100 秒（同之前一樣）；stream 靜默超過 45 秒當 stall（comment 都算有資料）。
- 錯誤分類（`IsTransient`）：timeout／stall、斷線（`IOException`、連唔到）、HTTP 408/429/5xx、200 入面 code 係 408/429/5xx 或 `server_error` 嘅錯誤、stream 入面嘅 `error` event 或 `finish_reason: "error"`、stream 未有 finish 就完 → `OpenRouterTransientException`（帶 `Retry-After`）或 `TimeoutException`。其他（400/401/402/403、3xx、`length`、`content_filter`、冇字、回覆 parse 唔到）照舊係唔可重試嘅錯誤。錯誤訊息只用 code，唔引 provider 原文。使用者自己取消係 `OperationCanceledException`，唔會變 timeout。
- `infrastructure/Notes/NoteProviderAdapters.cs`：`OpenRouterNoteWriter` 一定 streaming（刪咗「Not streamed」嗰段）。

### Live 驗證（授權內：最多 5 次，實際用咗 2 次）

指令（喺 repo root；`.env` 由 node 載入 process，冇顯示內容）：

```powershell
node --env-file-if-exists=.env app-temp/checks/Live/run-openrouter-stream-live.mjs
```

（喺 worktree 跑時用咗 main checkout 嘅 `.env` 絕對路徑。）三句合成英文課堂、Qwen3.8 Flash `@none`、冇音訊。結果：

| Call | 結果 | 時間 | Stream 觀察 | Usage |
| --- | --- | --- | --- | --- |
| 1. 經 `NoteWorkflow` 寫筆記（structured output + streaming + Activity） | completed，3/3 來源有筆記，openrouter/qwen | 第一個 byte 1.44 s；Activity 第一次見到 draft 2.95 s；完成 7.76 s；Activity 見到 13 個唔同 draft | `text/event-stream`；111 個 data event；最後係 `[DONE]`；usage 喺最後一個 JSON event（index 109），choices = `[{"index":0,"delta":{"content":"","role":"assistant"},"finish_reason":"stop","native_finish_reason":"stop"}]`；finish_reason `stop` 出現兩次；0 個 comment；provider 全部 Alibaba | 2,057 in（256 cached）/ 367 out，US$0.000447 |
| 2. 普通 streaming 回答（冇 schema） | 一句答案，3 次 draft 更新 | 第一個 byte 1.77 s，完 2.28 s | 11 個 data event，`[DONE]` 結尾，usage 喺最後 JSON event，同上格式 | 32 / 25，US$0.0000166 |

合計約 US$0.00046。文件講嘅格式全部對得上：usage 喺最後、finish 重複、structured output 可以 streaming。兩次都冇出 comment（回覆太快），所以 45 秒靜默上限未有真實數據支持，見「限制」。

## B. Fallback（選項 2）

- `infrastructure/Notes/NoteWriterFallback.cs`（新）：`FallbackNoteWriter` 包住 `OpenRouterNoteWriter` 同 `GeminiNoteWriter`。可重試錯誤重試，等 1 秒、4 秒（或者 provider 嘅 `Retry-After`，最多 20 秒）；最多 3 次；第一次之後 60 秒窗口過咗就唔再開新 OpenRouter attempt（避免 stall 食三個 100 秒 deadline）。用完就由 Gemini 用同一個 task、input、schema 同上限寫。冇 Gemini key 就報 OpenRouter 嘅錯誤加 attempt 數。其他錯誤同使用者取消唔重試、唔 fallback。重試時 draft 清空再開始。
- `NoteWriters.Create`：`openrouter:` writer 一律包 `FallbackNoteWriter`，fallback 係 `PLAYBACK_GEMINI_MODEL`（預設 `gemini-3.1-flash-lite`）。
- 實際 writer 點樣記錄：唔加新欄位。Usage 報告（Activity 本身已保存，一直都記 serving provider）喺需要多過一次 attempt 時加 `writer`：`{provider, model, fallbackFrom?, failures: [{error, usage?}]}`，失敗 attempt 嘅 usage（如有）都留低。`NoteActivityAdapter` 見到就將 Activity 嘅 provider／model 改做實際 writer（live popover 即時變），完成或失敗時 summary 加「· Fallback: written by vertex/gemini-3.1-flash-lite after openrouter/qwen/qwen3.8-flash failed 3 times: OpenRouter text HTTP 503」或者「· Retried: …」。Activity popover 本身顯示 `provider · model` 同 summary，所以 web 唔使改；保存紀錄（`activity` collection）有同樣嘅 provider、model、summary、usageJson。
- 冇改 `application/Activity`、domain 或 web。

## C. OpenCC 詞組

`infrastructure/Notes/OpenccNoteScript.cs` 加咗第二步：用 OpenCC 自己嘅 STPhrases（`DictionaryLib.Provider.st_phrases`，OpenccNetLib 內置，唔使改 csproj）做最長匹配分詞，同 OpenCC 一樣；一個全部由共用字組成嘅詞，要三樣都成立先轉：

1. `s2hk` 改嘅每個字都喺 `PhraseCharacters`（香港只寫繁體形嘅共用字：后于里干几余面制范准据征松丰只涂采筑柜尸舍仆郁胡姜咸杰托划斗丑谷云占杆杠夸御辟雇愈）；
2. 冇另一個詞由佢中間開始而伸出佢後面（分詞有疑問就唔轉）；
3. `s2hk` 唔改長度。

`s2hk` 本身經 HKVariants 已經保留部分香港寫法（考核、台下：臺→台、覈→核）。刻意唔放入清單：了（了解、明了、了如）、注（加注／注意）、布（公布、宣布）、周（上周、周末）、游、着、伙、升、岩、佣，同埋喺正確繁體太常見、分詞錯會出事嘅字（表、回、出、系、卷、致、板）。

### 點揀：用 Week 3 run 嘅真實輸出

- 第一步：喺 `app-temp/data/validation/runs/2026-10-18-*` 全部 `replies.json`、`note.json`、`notes.md` 跑純 STPhrases（共用字、會變）分詞。6,535 個詞入面真係出現嘅有：了解→瞭解 87 次、明了→明瞭 51（全部係「說明了」分錯詞）、了如→瞭如 31（「演示了如何」）、上周→上週 26、加注→加註 24（「多加注意」）、公布→公佈 20、考核→考覈 19、在核→在覈 12（「在核心」）、宣布→宣佈 6、台下→臺下 6；正例：在于、根据、制作、由于、精准、背后。即係直接用 OpenCC 詞組轉換會誤改大量正確香港繁體。
- 第二步：加上面三條規則之後，用 scratch 程式（引用 infrastructure project，冇入 repo）對同一批 4,218 段有中文嘅字串跑完整 `OpenccNoteScript.Apply`：**正確繁體冇一處被改**；新規則額外轉咗嘅只有 存在于／专注于→於、根据→根據、制作→製作、精准→精準、背后→背後（Gemini 繁體筆記入面嘅「其背后的原因」）。其餘改動都係本來已有嘅純簡體字轉換（例如 Qwen 推理式輸出入面嘅「讲师」「属于」）。
- 「示范」本身唔喺保存咗嘅 run 入面（上次比較時見到但冇存），所以 fixture 入面標明係合成。

`checks/Offline/NoteScriptChecks.cs` 嘅正反例：每句 run 例子都附 run folder；合成例子標 `synthetic`。

## 證據

```powershell
dotnet build app-temp/api/Playback.Api.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
# 0 Warning(s), 0 Error(s)
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
# exit 0；29 行全部 "passed"，包括：
# OpenRouter text checks passed (in-memory HTTP): fixed endpoint and key, note schema and caps, parameter-complete fp8+ routing, usage on rejection, safe errors, offline guard; SSE draft before the reply ends, usage from the last chunk, mid-stream error, dropped connection, silence and deadline as transient failures, caller cancellation, stream size limit
# Note script checks passed: 10 Simplified examples converted (phrases of shared characters included), 14 correct Hong Kong Traditional examples unchanged, other note languages untouched
# Note writer fallback checks passed (in-memory HTTP): 503 and mid-stream errors retried after 1 s and 4 s, Retry-After honoured and capped, Gemini writes after three transient failures or the retry window, other failures and cancellation not retried, activity names the actual writer live and saved, streamed draft visible in activity before the reply ends
```

`NoteEditChecks` 原本嘅 OpenCC 例子（P3 擁有）冇改，照樣通過。

## 範圍外改動

| 檔案 | 改動 | 原因 |
| --- | --- | --- |
| `app-temp/checks/Program.cs` | 離線 notes 組加 `NoteScriptChecks.Run()`、`NoteWriterFallbackChecks.Run()` 兩行；加 `--openrouter-stream-live` 一個 case | 新 check 檔案要喺度登記先會跑。其他 phase 都可能加行，衝突應該只係相鄰行 |

新增檔案（屬本 phase）：`infrastructure/Notes/NoteWriterFallback.cs`、`checks/Offline/NoteScriptChecks.cs`、`checks/Offline/NoteWriterFallbackChecks.cs`、`checks/Offline/OpenRouterHttpFakes.cs`、`checks/Live/OpenRouterStreamLiveCheck.cs`、`checks/Live/run-openrouter-stream-live.mjs`。冇改 `ModelOutputContract.cs`（唔使改）、`application/Activity`、domain、web。root `package.json` 冇加 npm script（唔屬本 phase）；合併時可以加 `"test:openrouter-stream-live": "node --env-file-if-exists=.env app-temp/checks/Live/run-openrouter-stream-live.mjs"`。

## 限制同未完成

- **45 秒靜默上限未經真實 comment 驗證**：兩次 live call 都快到冇出 `: OPENROUTER PROCESSING`。文件冇講 comment 幾耐出一次。如果長 organizer call 開頭排隊超過 45 秒而 OpenRouter 唔送 comment，會當 stall 重試。下面 live 計劃第 1 項會量。
- **取消唔慳錢**：Alibaba 唔支援 stream 取消，timeout／stall 之後重試或者 fallback，第一次嘅錢照收；而 provider 冇報 usage 時，失敗 attempt 嘅成本唔會出現喺 usage。
- **Fallback 用 routine Gemini model**（預設 `gemini-3.1-flash-lite`），唔係 2026-10-18 比較用嘅 `gemini-3.5-flash-lite`；要換就設 `PLAYBACK_GEMINI_MODEL`（會連 chat 等一齊換）。
- **最差延遲**：OpenRouter stall 一次（100 秒 deadline）然後 Gemini（100 秒 deadline）最多約 200 秒；5xx 類錯誤三次加等候約 5 秒就 fallback。
- **重試期間 cache**：Gemini fallback 冇 Qwen 嘅 prompt cache，嗰次 call 會貴啲（2026-10-18：Gemini 20 分鐘 US$0.119 vs Qwen US$0.027）。
- **OpenCC 清單係判斷**：上周／周末保留（香港兩種都有人寫）；`PhraseCharacters` 入面嘅字如果遇到 OpenCC 冇收錄、又同前一個字組成正確繁體詞嘅情況，仍然可能誤改。Week 3 語料冇出現；廣東話／其他科目語料（P2）可以再驗。
- `NoteModelComparison`（P2 擁有）用 `OpenRouterNoteWriter`，而家會 streaming；行為同成本應該一樣，但 P2 嘅 run 會反映 streaming。

## Live 驗收計劃（未做，要另外授權）

1. **長回覆同 comment 頻率**（OpenRouter，約 US$0.03）：`npm run test:note-models-live -- --models "openrouter:qwen/qwen3.8-flash@none|batch+organize" --folder app-temp/data/validation/runs/2026-10-xx-p4-streaming`（Week 3 頭 20 分鐘，約 28 次 call，同 2026-10-18 `placement-v13b` 比較）。量度：每次 call 第一個 draft 時間（Activity）、總延遲、有冇 timeout／stall／重試（usage 入面 `writer.failures`）、成本同 cache 命中率同之前比較。另外跑一次 `run-openrouter-stream-live.mjs` 改用 organizer 大 input，記錄 comment 到達時間，決定 45 秒係咪要調。
2. **實機 Activity**（P1 實機測試時順手睇，冇額外成本）：`npm run dev`，錄約 3 分鐘，打開 Activity popover，確認 Qwen draft 喺回覆完之前逐步出現，完成後 provider 顯示 `openrouter · qwen/qwen3.8-flash`。
3. **真 fallback**（1 次 Gemini call，約 US$0.005；要授權）：喺本機暫時令 `openrouter.ai` 連唔到（例如防火牆或 hosts 指去 127.0.0.1），用 `npm run dev` 觸發一次筆記。預期：3 次「OpenRouter could not be reached」（約 5 秒內），然後 Gemini 寫；Activity 顯示 `vertex · gemini-3.1-flash-lite`，summary 有「Fallback: written by vertex/… after openrouter/qwen/qwen3.8-flash failed 3 times」，MongoDB `activity` 紀錄同樣。完成後恢復網絡設定。

## 要使用者決定

1. 「上周」「周末」要唔要轉「上週」「週末」？而家保留（香港兩種寫法都常見）。
2. Fallback 用嘅 Gemini model：保持 routine model（3.1 Flash-Lite），定另設一個筆記 fallback model（例如 3.5 Flash-Lite，要加設定）？
3. 要唔要喺 root `package.json` 加 `test:openrouter-stream-live`（合併步驟處理）。
