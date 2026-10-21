# 三速筆記 live 驗收（Week 3 全堂，2026-10-22）

用 app 本身嘅 `NoteWorkflow`、本機 MongoDB（`playback_e2e`）同真筆記 writer（Qwen3.8 Flash → GLM-5.3-Flash → Gemini）跑 Week 3 全堂 164.7 分鐘，同 [POC](poc-three-speed-notes-asr-context-2026-10-21.zh-HK.md) 比較。全部 pass，總費用 **US$0.164**（上限 US$0.30）。

## 做法

- **真嘅部分**：`NoteWorkflow`＋`NoteScheduler` tick（同 API worker 一樣）、`NoteStoreAdapter` → MongoDB、`NoteWriters.Create` 嘅 fallback 鏈、OpenCC、`NoteCommands.LockBlock`、`Rebuild`、`WritePending`；網頁係真 API（offline 模式，冇 provider）＋Vite。
- **模擬嘅部分**：冇錄音、冇 ASR，只送文字。`transcript.txt` 每行字幕一個 transcript（1,266 行），`RecordedAt` 用字幕時間，`ConfirmedAt` 係講完後 3 秒（每行最多當 15 秒長）；字幕喺模擬時鐘到咗 `ConfirmedAt` 先寫入 DB，所以 jot／segment 喺錄音中觸發。Provider 延遲係真，但等候時間壓縮咗（42 分鐘跑完 165 分鐘）。Material 係 POC 嘅 course glossary（`course-context.json`），同 POC 一樣做 prompt 前綴。
- **步驟**：record（每分鐘 jot、每五分鐘 segment，再 Stop）→ edit（13:10 一行字幕 `Query file` 改做 `create file`，好似第二遍 ASR 咁存，然後 notes-only rerun `WritePending`）→ rebuild（鎖一個 block，再 Rebuild notes）→ report → browser。
- **指令**：`pnpm.cmd test:three-speed-live`（預設 offline，只讀保存咗嘅結果）；`-- --live --max-usd 0.3` 付費重跑（已有 `<phase>.json` 嘅步驟會重用，上限計埋 folder 入面之前所有 call）；`-- --dry` 用 fixture 模型、唔連網；`-- --browser` 開網頁檢查。Script：[`three-speed-live.mjs`](../app-temp/checks/Validation/three-speed-live.mjs)、[`ThreeSpeedLiveRun.cs`](../app-temp/checks/Validation/ThreeSpeedLiveRun.cs)、[`three-speed-live.check.mjs`](../app-temp/web/test/three-speed-live.check.mjs)。
- **證據**：`app-temp/data/validation/runs/2026-10-22-three-speed-live/`（git ignore）：`ledger.jsonl`（每個 call 嘅 token、費用、模型、fallback）、`results.json`、`record／edit／rebuild／browser.json`、`notes-after-stop.md`、`notes-final.md`、`segments.json`、`activity.json`、截圖。Session `a289adfffe1a4a259025b22ffcbbedc7` 留喺 `playback_e2e`。

## 結果：錄音＋Stop，同 POC 比較

| | POC Qwen3.8（2026-10-21） | **App live（Stop 後）** | Rebuild 後 |
| --- | --- | --- | --- |
| Calls | 209（course 1、live 162、segment 33、final 13） | 172（jot 128、segment 34、outline 1、polish 9） | 44（segment 33、outline 1、polish 10） |
| 邊個寫 | Qwen 209 | Qwen 116、GLM 55、Gemini 1 | Qwen 30、GLM 14 |
| Input（cache）／output tokens | 585,572（47%）／103,719 | 634,385（44%）／93,526 | 219,267（42%）／70,092 |
| 費用（USD） | 0.099 | **0.1075**（OpenRouter 報 0.0996＋Gemini 估 0.0079） | 0.0546 |
| Topics（頂層），層數 | 12（12），1 | 9（9），1 | 10（10），1 |
| 筆記字數（同一計法，唔計空白同引用） | 41,984 | 34,554 | 35,083 |
| 「(?)」 | 63 | 239 | 199 |
| 「講師／lecturer」每千字 | 0.18 | 0.38 | 0.29 |
| 有引用嘅分鐘 | 160／165 | **161／165**（有講嘢嘅 162 分鐘） | 160／165 |
| Summary／溫習問題 | — | 8／8 | 8／8 |
| 延遲中位數（最長） | live 6.7 s；segment 22.5 s（88 s） | jot 11.0 s（21 s）；segment 23.4 s（46 s） | — |

- **Stop → outline 完成 51.6 s**（Stop 先寫最後一個未完嘅窗口，再 outline）；全部 polish 完、Stop 確認 **187 s**。拖長嘅原因：第 8 個大題（偽造父進程，32 個 segment block、14.6k 字）Qwen polish 超過 100 秒上限 → Gemini fallback 用咗 108 秒 → 回覆被 polish guard 拒絕（引用唔夠八成或字數唔夠一半）→ 呢個大題保留 32 個未潤飾 block。其他 polish 最長 52 秒。
- **錄音中跟得上**：writer 喺 9,859 秒講課入面忙 2,323 秒（24%）；jot／segment 逐個排隊，延遲中位數 11 秒、最長 46 秒。
- 1:42–4:03 冇字幕超過一分鐘，app 當咗閒置，提早寫咗第 0 個窗口，之後再寫一次（所以 34 個 segment 對 33 個窗口），係設計行為，多咗 US$0.001。
- 內容：結構清楚（例如「操作步驟／判斷原則」），用 `NtQueryFile`、`ntoskrnl.exe` 等正確寫法，保留原本聽到嘅字。

## 結果：改逐字稿、Rebuild、網頁

| | 結果 |
| --- | --- |
| **改一行字幕**（13:10，第 2 個窗口 10:00–15:00） | 只重寫第 2 個 segment；只重新 polish 引用佢嘅 1 個大題；**其餘 8 個大題一字不改**。2 calls、US$0.0015、37.5 s（POC graph 更新：3 個窗口 4 calls、US$0.0037） |
| **Rebuild＋鎖定 block** | 鎖咗嘅 block（成個舊大題嘅潤飾文字，2.6k 字）**一字不改**保留，放咗入新大題「Windbg 環境設定與符號載入」；其他全部重寫。44 calls、US$0.0546、900 s（POC 完整重建 47 calls、US$0.053） |
| **網頁** | Topic tree 10 個大題齊；10 個 source chip（每個大題一個 grouped chip）、0 個「Source unavailable」；撳 chip（1:05:01–1:08:24）跳去對應字幕行並 highlight；手機闊度冇橫向捲動；冇 page error |

- 改咗嗰一行之後，嗰個大題仍然寫 `NtQueryFile`：同一窗口其他字幕都係講「query file」，只改一行唔足以推翻，屬合理。
- Rebuild 後嗰個新大題同時有鎖定嘅舊文字同新潤飾文字，內容重複；鎖定 block AI 唔會刪，要用戶自己解鎖或刪除。

## 失敗同 fallback

- 冇 call 失敗（activity 0 個 failed）。
- **Qwen3.8 Flash 經常 429**（Alibaba）：69 個 call（218 個入面 32%）第一次 429，即刻交 GLM-5.3-Flash 寫好；錄音頭 60 分鐘 56 個 call 有 41 個係 GLM，90 分鐘之後 86 個有 84 個係 Qwen（429 似係一段時間嘅限流，唔係每次都有）。GLM 寫嘅部分冇另外評質素。
- 1 個 Qwen polish 超過 100 秒 → Gemini（見上）。中斷咗嘅 Qwen stream 冇 usage，OpenRouter 有冇收費唔知，ledger 冇計。
- Gemini 費用係估算（Flash-Lite list price；該 call 嘅 token 由 activity 記錄補返，因為 harness 初版讀錯 Gemini 嘅 usage 欄位，已修正）。

## 發現（未改 code）

1. **Outline 仍然冇子題**：9–10 個平排大題、層數 1；polish 喺 block 入面自己加 a)／b) 小標題，但 topic tree 只有一層。
2. **大題太大時 polish 會超時**：一個大題超過大約 30 個 block，Qwen 有機會過 100 秒；Gemini fallback 嘅回覆又過唔到 guard，結果嗰段冇潤飾。可以按 block 數拆開 polish，或者 polish 用較長 deadline。
3. **「(?)」太多**：239 個（POC 63），103 個喺同一個 Windbg 示範大題（`dd` (?)、`1D0` (?)、`KeServiceDescriptorTable` (?)），因為 prompt 要求重構嘅指令同數字都標。謹慎但影響閱讀。
4. **筆記短咗**：34.6k 字，POC 42.0k。
5. **Rebuild 要 15 分鐘**：`WriteDue` 逐個窗口寫；並行（最多 4 個 call）可以快大約四倍。
6. Qwen 429 時段性出現，今次大約三成 call 由 GLM 寫；要睇 GLM 筆記質素，或者同 OpenRouter／Alibaba 確認 rate limit。

## 費用

| | Calls | USD |
| --- | --- | --- |
| 錄音＋Stop | 172 | 0.1075 |
| 改逐字稿 | 2 | 0.0015 |
| Rebuild | 44 | 0.0546 |
| **合共** | **218** | **0.1636**（OpenRouter 報 0.1556＋Gemini 估 0.0079） |

Dry run（fixture 模型，265 calls）冇費用。冇送音訊。
