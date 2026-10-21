# P4 結果：筆記 provider（streaming、fallback、OpenCC 詞組）

Branch：`phase/p4-note-provider`（由 `main` 3cecbe0 開；2026-10-20 合併咗 `main` fd632b3，即 P3）。日期：2026-10-19、2026-10-20（第二輪：Qwen 限流 → GLM → Gemini、live 驗收）。

## 摘要

| 目標 | 結果 | 離線 check |
| --- | --- | --- |
| A. OpenRouter streaming | OpenRouter 筆記 writer 改用 SSE，Activity 嘅 live draft 邊收邊顯示。Week 3 頭 20 分鐘 31 次 call：第一段內容中位數 6.6 秒到，成個回覆 11.5 秒，即係 draft 早大約 5 秒出現 | `OpenRouterTextChecks`（SSE、斷線、靜默、deadline、取消、大小上限、stream 計時）、`NoteWriterFallbackChecks.LiveDraftWhileStreaming`（經真 `NoteWorkflow`，回覆未完 Activity 已見 draft） |
| B. Fallback（選項 2；第二輪加 GLM） | 三層：Qwen3.8 Flash → GLM-5.3-Flash（Qwen 可重試錯誤即刻轉）→ Gemini。最多 3 次 OpenRouter attempt、60 秒窗口；回頭再試同一個 model 先等 1 秒／4 秒或者 Retry-After。Activity 即時同保存紀錄都顯示實際 writer、model、serving provider 同原因 | `NoteWriterFallbackChecks`（重試、Retry-After、窗口、fallback、唔重試嘅錯誤、取消、workflow 保存紀錄、三層次序） |
| C. OpenCC 詞組 | 全部由共用字組成嘅簡體詞（示范、根据、由于、背后、制作、精准）而家會轉；香港用法（了解、公布、宣布、台下、干擾、公里、游泳、着）保留；上周／周末唔強制轉（使用者決定：靠 writer prompt）。Week 3 run 4,218 段真實輸出：正確繁體零誤改 | `NoteScriptChecks`（10 個轉換例、14 個保留例，大部分來自 Week 3 run） |

`dotnet build` 同全部離線 checks（30 組）通過。冇改 web，所以冇跑 web build／browser checks。冇改 prompt 文字，prompt 版本唔使升。Live 合計 US$0.034（上限 US$0.06），冇撞 429。

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

合計約 US$0.00046。文件講嘅格式全部對得上：usage 喺最後、finish 重複、structured output 可以 streaming。兩次都冇出 comment（回覆太快）；keep-alive 同 45 秒靜默上限喺第二輪 Week 3 run 量咗，見「Live 驗收」。

## B. Fallback（選項 2）

- `infrastructure/Notes/NoteWriterFallback.cs`（新）：`FallbackNoteWriter` 包住 `OpenRouterNoteWriter` 同 `GeminiNoteWriter`。可重試錯誤重試，等 1 秒、4 秒（或者 provider 嘅 `Retry-After`，最多 20 秒）；最多 3 次；第一次之後 60 秒窗口過咗就唔再開新 OpenRouter attempt（避免 stall 食三個 100 秒 deadline）。用完就由 Gemini 用同一個 task、input、schema 同上限寫。冇 Gemini key 就報 OpenRouter 嘅錯誤加 attempt 數。其他錯誤同使用者取消唔重試、唔 fallback。重試時 draft 清空再開始。
- `NoteWriters.Create`：`openrouter:` writer 一律包 `FallbackNoteWriter`，fallback 係 `PLAYBACK_GEMINI_MODEL`（預設 `gemini-3.1-flash-lite`）。
- 實際 writer 點樣記錄：唔加新欄位。Usage 報告（Activity 本身已保存，一直都記 serving provider）喺需要多過一次 attempt 時加 `writer`：`{provider, model, fallbackFrom?, failures: [{error, usage?}]}`，失敗 attempt 嘅 usage（如有）都留低。`NoteActivityAdapter` 見到就將 Activity 嘅 provider／model 改做實際 writer（live popover 即時變），完成或失敗時 summary 加「· Fallback: written by vertex/gemini-3.1-flash-lite after openrouter/qwen/qwen3.8-flash failed 3 times: OpenRouter text HTTP 503」或者「· Retried: …」。Activity popover 本身顯示 `provider · model` 同 summary，所以 web 唔使改；保存紀錄（`activity` collection）有同樣嘅 provider、model、summary、usageJson。
- 冇改 `application/Activity`、domain 或 web。

## B2. 第二輪：Qwen 限流時轉 GLM（2026-10-20）

背景：P3 嘅 live run 撞到 `qwen/qwen3.8-flash` 嘅 HTTP 429（provider Alibaba、`provider_error_code: limit_burst_rate`、`limit_source: upstream_provider_shared_pool`、`is_byok: false`、冇 Retry-After），即係 OpenRouter 喺 Alibaba 嘅共用容量限流。

### 查證（2026-10-20 讀）

- [Provider selection](https://openrouter.ai/docs/guides/routing/provider-selection)：`provider` 有 `order`、`allow_fallbacks`（預設 true）、`only`、`ignore`、`data_collection`（預設 allow）、`zdr`、`require_parameters`（預設 false）、`quantizations`、`sort`、`max_price`。預設已經會 fallback 去其他 provider；`order` 列出嘅 provider 試完都會再試其他，除非 `allow_fallbacks: false`。base slug（`alibaba`）會配對嗰個 provider 嘅所有 endpoint。文件冇講 429 係咪觸發轉 provider，亦冇講 streaming 加 structured output 時點轉；[errors](https://openrouter.ai/docs/api-reference/errors) 講明已經送出部分輸出之後唔會再轉。回覆嘅 provider 喺每個 chunk 頂層 `provider`、model 喺 `model`。
- `GET https://openrouter.ai/api/v1/models/qwen/qwen3.8-flash/endpoints`（公開、冇 key）：**只有 Alibaba**，兩個 endpoint（`alibaba/us-east-1` 有 implicit caching；`alibaba`），所以 provider routing 幫唔到。
- [Model fallbacks](https://openrouter.ai/docs/guides/routing/model-fallbacks)：`models` 陣列，任何錯誤（包括 rate limit）都會轉下一個 model，按實際用嘅 model 收費，回覆 `model` 欄位報實際 model。冇講 streaming／structured output。

### 次序：Qwen → GLM → Gemini（唔用 DeepSeek）

按 [2026-10-18 比較](../note-model-comparison-2026-10-18.md)：

- **GLM-5.3-Flash 排第二**：成功嘅 batch 寫得準確易讀；OpenRouter 上有 14 個支援全部參數（structured outputs、reasoning）而且 fp8 或以上嘅 provider（Reka、StreamLake、Near AI、Inceptron、DigitalOcean、Makora、PrimeIntellect、BaseTen、Fireworks、Friendli、Together、Venice、Morph 等，2026-10-20 endpoints list）。佢喺 v9 合約下 8 次 call 有 5 次失敗，但嗰陣係舊合約令所有 model 都出事；而家嘅 v13+ 合約未有大量數據（下面 live 一次成功）。
- **DeepSeek V4.1 Flash 唔放**：v10b 入面幾個 provider 交出缺標題、缺內容、甚至 key 損壞（`operations": 2.0,`）嘅操作；reasoning 關咗又幾乎全部 defer；reasoning low 一次 call 要 36 秒。損壞回覆 parse 唔到係唔可重試錯誤，會令成次 call 失敗而唔會去到 Gemini，所以 Gemini 做最後一層比 DeepSeek 穩陣。

### 點解唔係 OpenRouter 嘅 `models` 單一 request

先照計劃實作咗 `models: ["qwen/qwen3.8-flash", "z-ai/glm-5.3-flash"]`，live 驗證（Alibaba 用 `provider.ignore` 排除）即刻 HTTP 400：「Reasoning is mandatory for this endpoint and cannot be disabled.」。一個 request 只有一個 `reasoning` 設定；Qwen 要 `none` 先得 10 秒左右（`low` 要約 90 秒），GLM 唔接受 `none`。更差嘅係：Alibaba 真係限流時，OpenRouter 會交返 GLM 嘅 400，400 唔可重試，於是連 Gemini 都去唔到。所以改為每個 model 一個 request（`FallbackNoteWriter` 入面依次試），GLM 用 `reasoning: low`（2026-10-18 比較用嘅設定）。效果同使用者揀嘅一樣：Qwen 之後 GLM，再之後 Gemini，實際 model 同 provider 記低。代價係轉 model 多一個 round trip（Qwen 429 通常一秒內返嚟）。

### 改動

- `NoteWriters.ModelFallbacks`：`qwen/qwen3.8-flash` → `[(z-ai/glm-5.3-flash, low)]`。`NoteWriters.Create` 建 `FallbackNoteWriter([Qwen@none, GLM@low], Gemini)`。
- `FallbackNoteWriter`：attempt k 用第 ((k−1) mod n) 個 model；轉去另一個 model 唔等，回頭試已經試過嘅 model 先等 1 秒、4 秒或者嗰個 model 上次嘅 Retry-After（上限 20 秒）；最多 3 次、60 秒窗口；之後 Gemini。即係預設：Qwen → GLM → Qwen（等 1 秒）→ Gemini。`writer.failures` 每項記 `model`。
- Provider routing：Qwen request 保留 `order: ["alibaba"]`，`allow_fallbacks` 唔設（預設 true）；GLM request 冇 `order`，由 OpenRouter 喺合資格 provider 揀。冇加 `data_collection`／`zdr`（使用者冇要求）。
- 記錄：`OpenRouterTextModel` 嘅 usage 報告加 `model`（回覆嘅 model）同 `stream`（`headersMs`、`firstContentMs`、`keepAlives`、`longestGapMs`、`totalMs`）。`NoteWriterReport.Describe` 由 usage 讀出實際 writer／model 同 serving provider，`NoteActivityAdapter` 只對 OpenRouter 開始嘅 call 用佢：Activity 嘅 provider／model 變做實際嗰個，summary 加「Served by Alibaba」、「Fallback: written by openrouter/z-ai/glm-5.3-flash after openrouter/qwen/qwen3.8-flash failed 1 time: OpenRouter text HTTP 429 · Served by Morph」之類。

### 私隱：課堂內容會去邊

每次筆記 call 送出成段課堂文字（lecture window 最多 20 萬字）加筆記。

- 正常：OpenRouter → Alibaba（Qwen3.8 Flash；endpoints list 冇資料政策欄位）。
- Qwen 失敗：OpenRouter → GLM-5.3-Flash 嘅其中一個合資格 provider，由 OpenRouter 揀（上面 14 個；live 嗰次係 Morph）。用預設 `data_collection: allow`，即係 OpenRouter 文件所講「可能非暫時保存資料、可能用嚟訓練」嘅 provider 都可以收到。
- 再失敗：Google Vertex AI（Gemini，`PLAYBACK_GEMINI_MODEL`，預設 `gemini-3.1-flash-lite`）。
- 要收緊可以喺 GLM request 加 `data_collection: "deny"` 或 `zdr: true`，但要先確認仲有 provider 剩低（見「要使用者決定」）。

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
# exit 0；30 行全部 "passed"（合併 main 之後同第二輪改動之後各跑一次），包括：
# OpenRouter text checks passed (in-memory HTTP): fixed endpoint and key, note schema and caps, parameter-complete fp8+ routing, usage on rejection, safe errors, offline guard; SSE draft before the reply ends, usage from the last chunk, mid-stream error, dropped connection, silence and deadline as transient failures, caller cancellation, stream size limit
# Note script checks passed: 10 Simplified examples converted (phrases of shared characters included), 14 correct Hong Kong Traditional examples unchanged, other note languages untouched
# Note writer fallback checks passed (in-memory HTTP): 503 and mid-stream errors retried after 1 s and 4 s, Retry-After honoured and capped, Gemini writes after three transient failures or the retry window, other failures and cancellation not retried, activity names the actual writer live and saved, streamed draft visible in activity before the reply ends; Qwen (reasoning none) → GLM (reasoning low) → Gemini in order, with the answering model and serving provider in the activity
```

第二輪新加嘅離線 check：

- `OpenRouterTextChecks`：usage 有 `model` 同 `stream` 計時（keep-alive 數目、第一段內容、最長間隔）；Qwen request 嘅 provider routing（`order: alibaba`、冇 `allow_fallbacks: false`、`require_parameters`、冇 `models`）。
- `NoteWriterFallbackChecks.ThreeLayers`：經真 `NoteWorkflow`，用 `NoteWriters.ModelFallbacks` 建 writer，三個情況：Qwen 答（Activity：openrouter／qwen、「Served by Alibaba」）；Qwen 429 → GLM 答（request 次序 `qwen@none`、`glm@low`，冇等候；Activity：openrouter／z-ai/glm-5.3-flash、「Fallback: written by openrouter/z-ai/glm-5.3-flash after openrouter/qwen/qwen3.8-flash failed 1 time: OpenRouter text HTTP 429 · Served by Fireworks」，usage 記低 Qwen 嘅失敗）；全部 429 → Qwen、GLM、Qwen（等 1 秒）→ Gemini（Activity：vertex／gemini）。`NoteWriters.Create` 嘅預設 writer 係 `[qwen, glm]` 加 Gemini。

`NoteEditChecks` 原本嘅 OpenCC 例子（P3 擁有）冇改，照樣通過。

## Live 驗收

規則：使用者批准合計上限 US$0.06（包括之前用咗嘅）；429 用 P3 `RateLimitGuard`（頭 10 個 writer call 有 3 個 429 就停）；失敗最多重跑一次。`.env` 由 node 載入 process，內容冇顯示；喺 worktree 跑時用 main checkout `.env` 嘅絕對路徑。

### 費用

| 日期 | 項目 | Request | 費用（US$） |
| --- | --- | --- | --- |
| 10-19 | 細 streaming call ×2（Qwen 筆記、Qwen 普通答案） | 2 OpenRouter | 0.000463 |
| 10-20 | Week 3 頭 20 分鐘 Qwen run | 28 writer + 3 organizer（OpenRouter） | 0.0311 |
| 10-20 | 細 call 1、2：`models` 單一 request（Alibaba 排除） | 2 OpenRouter，HTTP 400 | 0（400 冇生成） |
| 10-20 | 細 call 3：Qwen 429（本機模擬，冇送出）→ GLM | 1 OpenRouter | 0.000503 |
| 10-20 | 真 fallback：OpenRouter 連唔到 → Gemini | 0 OpenRouter，1 Vertex | ≤0.0019（估計：3,473 in／328 out，用 3.5 Flash-Lite 牌價做上限；Vertex 冇報 cost） |
| | **合計** | | **≤0.034** |

授權嘅 3 次細 call 用晒（兩次 400 都計）。冇 429，RateLimitGuard 冇觸發（冇 `rate-limits.jsonl`）。

### 1. Week 3 頭 20 分鐘 Qwen run（streaming + keep-alive）

```powershell
$env:PLAYBACK_NOTE_MODELS_STOP_ON_429="yes"; $env:PLAYBACK_RATE_LIMIT_LOG="<folder>/rate-limits.jsonl"
node --env-file-if-exists=.env app-temp/checks/Validation/run-note-models.mjs --live --models "openrouter:qwen/qwen3.8-flash@none|batch+organize" --folder app-temp/data/validation/runs/2026-10-20-p4-qwen-stream
```

（比較 runner 係 P2 擁有，直接用 `OpenRouterNoteWriter`，即係量 Qwen 本身，唔經 GLM／Gemini fallback。）Run folder：`app-temp/data/validation/runs/2026-10-20-p4-qwen-stream`（本機，冇 commit）。Prompt `tree-notes-v15`／`tree-organize-v19`。

| 量度 | 結果 |
| --- | --- |
| Writer calls 完成／總數 | 28 / 28（另有 3 次 organizer，全部完成） |
| 來源引用 | 58 / 58 |
| 延遲 median／max | 11.6 s / 18.4 s |
| Tokens in（cached）／out | 252,545（88,832，35%）/ 10,821 |
| 費用 | US$0.0311（2026-10-18 v13b：US$0.027） |
| 主題（空）／段 | 19（2）/ 54 |
| 模型原始輸出簡體字 | 3（runner 量原始輸出，冇經 OpenCC） |

Stream 計時（31 次 call，usage 入面 `stream`）：

| | min | median | max |
| --- | --- | --- | --- |
| Response headers（ms） | 1,201 | 6,647 | 8,154 |
| 第一段內容（ms） | 1,203 | 6,649 | 8,155 |
| 成個回覆（ms） | 3,775 | 11,482 | 19,616 |
| 行與行最長間隔（ms） | 283 | 334 | 569 |
| Keep-alive comment | 0 | 0 | 1 |

發現：OpenRouter 等到 provider 開始出字先送 headers（headers 同第一段內容差唔到 5 ms），之後字一直流，最長間隔唔夠 0.6 秒。即係 draft 喺 call 開始後中位數 6.6 秒出現，之前要等 11.5 秒。45 秒靜默上限只管 headers 之後，實際最長間隔 0.57 秒，非常寬鬆；headers 之前嘅等候由 100 秒 call deadline 管。Activity summary 每次都有「Served by Alibaba」。

### 2. 細 call：model fallback 同 streaming + structured output（`run-openrouter-stream-live.mjs models`）

- 第一次（`models` 單一 request，`provider.ignore: ["alibaba"]`）：HTTP 400，冇 body 記錄；加記錄後重跑一次：「Reasoning is mandatory for this endpoint and cannot be disabled.」→ 改設計（見 B2）。
- 第三次（新設計）：Qwen request 喺本機回一個同 P3 一樣嘅 429（`limit_burst_rate`、`upstream_provider_shared_pool`），冇送出；GLM request（`reasoning: low`、`stream: true`、strict json_schema、`require_parameters`）送出：HTTP 200，`text/event-stream`，127 個 data event，`[DONE]` 結尾，usage 喺最後 JSON event（同 Qwen 一樣格式，finish `stop` 出兩次），2 個 `: OPENROUTER PROCESSING` comment（3.9 s、4.7 s，喺內容中間），serving provider **Morph**，model `z-ai/glm-5.3-flash`。Headers 1.48 s、第一個 draft 2.78 s、完成 6.98 s，Activity 見到 12 個唔同 draft，drafting 時 Activity 已經顯示 `openrouter/z-ai/glm-5.3-flash`。3/3 來源有筆記，繁體。2,020 in（0 cached）/ 249 out，US$0.000503。保存嘅 summary：「… · Fallback: written by openrouter/z-ai/glm-5.3-flash after openrouter/qwen/qwen3.8-flash failed 1 time: OpenRouter text HTTP 429 · Served by Morph」。

### 3. 真 fallback（`run-openrouter-stream-live.mjs fallback`）

OpenRouter 經一個死咗嘅本機 proxy（127.0.0.1:9）連，即係連唔到 openrouter.ai（同計劃講嘅封鎖網絡一樣效果，但唔使改系統設定，亦冇 request 離開部機）。結果：Qwen、GLM、Qwen（等 1 秒）三次「OpenRouter could not be reached」，然後真 Gemini（Vertex）寫：completed，3/3 來源，Activity drafting 時已顯示 `vertex/gemini-3.1-flash-lite`，保存 summary「… · Fallback: written by vertex/gemini-3.1-flash-lite after openrouter/qwen/qwen3.8-flash failed 3 times: OpenRouter could not be reached」，usage 嘅 `writer.failures` 有三個 model 同錯誤。總時間 10.4 s。

用 dotnet 直接跑 live check（唔使 npm script；key 要喺 process environment）：

```powershell
$env:PLAYBACK_OPENROUTER_STREAM_LIVE="yes"
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/ -- --openrouter-stream-live stream   # 或 models、fallback
```

## 範圍外改動

| 檔案 | 改動 | 原因 |
| --- | --- | --- |
| `app-temp/checks/Program.cs` | 離線 notes 組加 `NoteScriptChecks.Run()`、`NoteWriterFallbackChecks.Run()` 兩行；加 `--openrouter-stream-live <mode>` 一個 case | 新 check 檔案要喺度登記先會跑 |

新增檔案（屬本 phase）：`infrastructure/Notes/NoteWriterFallback.cs`、`checks/Offline/NoteScriptChecks.cs`、`checks/Offline/NoteWriterFallbackChecks.cs`、`checks/Offline/OpenRouterHttpFakes.cs`、`checks/Live/OpenRouterStreamLiveCheck.cs`、`checks/Live/run-openrouter-stream-live.mjs`。冇改 `ModelOutputContract.cs`、`application/Activity`、domain、web、root `package.json`（使用者決定唔加 npm script）。

## 限制同未完成

- **GLM 只有一次 live 數據**：新合約下 GLM 只寫過一次（3 個來源）。長課堂、organizer call 未試；如果 GLM 交出 parse 唔到嘅回覆，嗰次 call 會失敗（唔會再去 Gemini）。
- **GLM 冇 cache**：轉去 GLM 嗰幾次冇 Qwen 嘅 prompt cache（live 嗰次 0 cached），亦冇固定 provider，所以 cache 都難中。
- **取消唔慳錢**：Alibaba 唔支援 stream 取消；timeout／stall 之後轉 model 或者 Gemini，第一次嘅錢照收。
- **Gemini fallback 用 routine model**（預設 `gemini-3.1-flash-lite`）；Vertex 唔報 cost，上面係估計。
- **最差延遲**：Qwen stall（100 s）→ 60 秒窗口已過 → 直接 Gemini（100 s）最多約 200 秒；429／5xx 類錯誤三次加等候約 1 秒就 fallback。
- **OpenCC 清單係判斷**：`PhraseCharacters` 入面嘅字遇到 OpenCC 冇收錄、又同前一個字組成正確繁體詞嘅情況，仍然可能誤改；Week 3 語料冇出現。
- `NoteModelComparison`（P2 擁有）用 `OpenRouterNoteWriter`，會 streaming，但唔經 GLM／Gemini fallback；比較 run 量嘅係單一 model。
- P1 實機 Activity 檢查今次冇做（P1 未開始）。

## 要使用者決定

1. GLM request 要唔要收緊私隱：`data_collection: "deny"` 或者 `zdr: true`？加之前要先查 GLM 仲有冇 provider 剩低（要一次公開 endpoints 查詢，唔收費）。
2. 45 秒靜默上限：live 最長間隔 0.57 秒，可以收緊（例如 20 秒）令 stall 更快轉 model；定保持？
3. GLM 喺長課堂同 organizer 未有數據：要唔要用比較 runner 跑一次 `openrouter:z-ai/glm-5.3-flash@low|batch+organize`（Week 3 頭 20 分鐘，估計 US$0.02–0.04）先當佢係可靠嘅第二層？
