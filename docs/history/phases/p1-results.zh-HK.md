# P1：Web 同三分鐘實機錄音驗收

2026-10-20。使用者批准一次約三分鐘錄音／ASR／筆記測試，同啟動獨立 MongoDB container。**測試已做，但未達到完整 live 驗收。** 冇為咗變綠而改寫原始逐字稿或資料庫筆記。

## 結果

- Web production build 通過。離線 browser checks：note-tree、note-instruction、note-rewrite、processing-settings、library-sidebar、notes-citations-chat、notes-reading-sources、session-loading、markdown-math、cold-loading、transcript-recording 全部通過。
- 使用 repo `sampleAudio.m4a` 播放 Windows system loopback，真正錄音三分鐘，送現有 ASR，經 API／Mongo 保存 Qwen 筆記。
- 從 capture 開始到第一段 transcript：126,954 ms；到第一份 note：131,993 ms。**未達約 40 秒目標**。呢兩個係 capture-start latency，唔冒充「講完後」嘅精確量度。
- 最早兩次 ASR 送出失敗，之後首兩次成功 ASR 嘅 provider latency 約 54.9／53.8 秒；第一個 Qwen note call 約 2.15 秒。呢次主要等候出現在 ASR 前段，不能歸因為 Qwen writer 本身花咗 132 秒。
- 最初兩段 ASR 連線提前結束；保留音訊，重啟後成功辨識。後續 snapshot 有 13 completed、2 pending、3 deferred（「嗯。」等語助詞）及 1 skipped 空白來源。原有 pending 開場片段係 `This year.` 同 `I have tried to using AI to assist me to do the cleaning.`。
- Saved Activity 顯示 `openrouter · qwen/qwen3.8-flash`、Alibaba serving provider 同真實錯誤。短錄音出現 organization due；未取得成功 organizer 重組嘅 live 證據。
- 最終保存文字為繁體；引用／desktop／375px 用實際保存 session 做 browser 檢查，唔呼叫生成。截圖：`output/playwright/wave1-live/desktop-activity.png`、`mobile-notes.png`；結果：同目錄 `results.json`。

## 揭出並修正嘅問題

遲到嘅 ASR 開場片段要補入已滿主題，但 placement schema 排除已滿主題。模型改用 `new1`，卻冇喺 outline 宣告；application 正確拒絕 unknown topic，因此兩段繼續 pending。重試相同 contract 冇解決問題。

修正 `ModelOutputContract`：未鎖定嘅現有主題，包括 full topic，仍可作 block 目標。「滿」係 organizer review 門檻，符合 P3 已決定嘅行為。`tree-notes-v16` 明確要求新 key 先宣告、rename 保留原 ID，以及相關內容可留喺 full topic。唔增加自動 model call，唔自動編造主題名。

離線 regression 驗證 malformed reply 保留 pending，正確 reply 可以補回；schema 檢查先紅後綠。已經在途嘅最後一次小型 live diagnostic 使用同一份保存輸入、production workflow、in-memory store，成功處理兩段，0 pending。證據 `diagnostic-v16-schema-*`。**呢個唔係 Mongo session 已修復，亦唔係重新跑完錄音 E2E。** 使用者質疑成本後，冇再啟動付費修復。

另修正 storage 同 fixture 嘅 organization status projection：按 source 時間計算八分鐘滿嘅狀態，唔再只計 blocks。新增 status-before/status-after check。

## 重現與邊界

本機證據：`app-temp/data/validation/runs/2026-10-20-wave1-p1-recording/`，session `0d760f1739d14171a350cb2ade6db0c2`。初始同 recovered snapshots、失敗 Activity、raw diagnostic replies 全部保留。

只讀 browser（載入保存資料，禁止 providers／背景掃描）。需要本機保留嘅 evidence／test database；獨立 MongoDB container 已停止，重開需當次授權。新 checkout 先編譯 wrapper 指定嘅 output：

```powershell
dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/wave1-fixed/net10.0/
node app-temp/checks/Live/run-wave1-recording.mjs --inspect
```

獨立 container `playback-wave1-20261020`、volume `playback_wave1_20261020`、`127.0.0.1:27019/playback_e2e`；原 27017 資料庫未改。Mongo note-store 整合檢查亦通過（125 版、restore、CAS／Stop），唔代表真實筆記語義已驗收。冇刪除 volume／user files。

未完成：40 秒 latency、修正後完整自然錄音 E2E、成功 organizer live 重組。ASR 準確度未有人工 ground truth 評分，不能用引用齊全取代。

範圍外改動：`NoteInstructions.cs`、`ModelOutputContract.cs`、domain 註解及 `NoteEditChecks` 屬實測揭出嘅 placement contract 修正；Storage／MemoryStore／OrganizationChecks 屬 M 收尾；checks Live／Program 登記新增實機驗證入口。本輪一併提交至 main，未 push。
