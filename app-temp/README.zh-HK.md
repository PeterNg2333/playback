# Playback 本機 prototype

錄低大約三小時嘅課堂或者會議，即時出逐字稿同 AI 筆記，事後可以就內容提問，答案連返原話。產品要求見 [business requirements](../docs/business-requirements.md)；code 點分見 [STRUCTURE.md](STRUCTURE.md)；以前每輪改動同驗收記錄見 [docs/history](../docs/history)。

呢個係 prototype，唔係已驗收嘅產品：冇三小時實機穩定性保證，冇講者分離，只可以附純文字教材。

## 點樣運作

```text
Browser 錄音（咪高峰／分享螢幕嘅聲）── 上載 ─┐
Windows 咪高峰／系統聲（API 部機）───────────┴─ .NET API ── 每段最多約 8 秒 WAV（本機磁碟）
                              ├─ ASR（OpenRouter Qwen3-ASR，或 SenseVoice）→ 逐字稿（MongoDB）
                              ├─ 筆記（Qwen3.8 Flash，經 OpenRouter；冇 key 就用 Gemini）
                              ├─ 翻譯、術語解釋、Ask（Gemini；術語排序用 Jev）
                              └─ 可選：LightRAG concept graph sidecar（graph/）
React/Vite 網頁 ── 同一個 API
```

- **錄音**：開始前揀 Microphone、System audio（全機播放聲）或兩者；兩個來源分開保存。Silero VAD 判斷人聲，靜音唔送 ASR，原始錄音一定保留。錄音有兩種，API 兩種都提供時，錄音掣旁邊有「Recorder」可以揀：
  - **Browser**：網頁自己錄，再上載去 API，雲端部署（Linux）只有呢種。咪高峰用 browser 權限；System audio 要分享螢幕：Chrome／Edge on Windows 揀「整個螢幕」再勾「同時分享系統音訊」（分享一個 tab 就只有嗰個 tab 嘅聲）。手機 browser 只可以錄咪高峰。每段 16 kHz mono WAV 先存喺 browser（IndexedDB），按次序上載；斷線會重試，重載頁面之後會繼續補傳，錄音掣旁邊 ↑N 係未上載嘅段數。暫停唔會閂咪同分享，繼續錄唔使再揀螢幕。限制：冇灰字預覽（正式逐字稿到咗先顯示）；錄緊嗰段未封存就閂 tab 會失去嗰幾秒；停止分享螢幕就冇系統聲。
  - **Windows (API)**：運行 API 嗰部 Windows 電腦用 WASAPI 錄，網頁重載唔會停錄音，有灰字預覽。
- **逐字稿**：每段封存後自動送 ASR，最多兩個並行；失敗會重試，之後可以手動重試。錄音期間約每 2 秒有灰字預覽，正式稿到咗就取代。原文同顯示文字（例如繁體轉換）分開保存。
- **Session rerun**：停錄音之後可以將已保存音訊合成 20–90 秒窗口再辨識；新結果成為一個 segment，取代舊列但保留第一遍文字。**Lineage** 將筆記引用同術語映射到新 segment。
- **筆記（三速）**：錄音期間每完成一分鐘寫一段 live jot，每完成五分鐘寫一個 segment（筆記、概念、ASR 更正）；按 Stop 之後先整理成大綱，再逐個主題潤飾。逐字稿改咗（第二遍 ASR 或 rerun）嘅五分鐘窗口會重寫，只有引用佢嘅主題會重新潤飾，其他主題唔郁。可以鎖定 block、對全份／主題／block 下指令、Rebuild、睇歷史同還原；人手改過或者鎖咗嘅 block 唔會俾 AI 換走。繁體筆記用 OpenCC 轉走簡體字。設計同費用見 [POC 結果](../docs/poc-three-speed-notes-asr-context-2026-10-21.zh-HK.md)。
  - 舊版筆記（placement 年代 block 來源喺 `Anchors`；pre-tree 主題只有 Markdown；最早嘅版本只有成份 `Note.Markdown`）App 唔會自動轉換，要跑一次 data patch：`dotnet run --project app-temp/checks -- --legacy-note-patch`（預設 dry run，只報告；加 `--apply` 先將會改嘅 document 原文備份去 `app-temp/data/backups/` 再寫入；`--legacy-note-patch-rollback <備份檔>` 還原）。Patch 之後 block 有來源，只有 Markdown 嘅版本按標題拆成主題，舊模型留低嘅假引用（`[cite_T009]` 呢類，旁邊已經有真引用）會拎走；舊版本都可以還原（本機 `playback_prototype` 已於 2026-10-22 套用）。想由逐字稿重寫就喺筆記選單揀 Rebuild notes。
- **術語、翻譯、Ask**：Jev 揀值得解釋嘅術語，Gemini＋Google Search 寫解釋；翻譯每批十段；Ask 只可以引用本 session 嘅來源，可以另外開網絡搜尋。開咗 concept graph 嘅話 Ask 先由圖揀段落。
- **Activity**：筆記標題旁邊嘅 ↶ 顯示每次 AI 呼叫嘅模型、狀態、時間同錯誤。

## 執行

需要 .NET 10 SDK、Node.js、Docker Desktop（本機 MongoDB）。喺 repo root：

```powershell
pnpm.cmd dev        # 首次會安裝套件；啟動 MongoDB container（如有現成 image）、API 同 Vite
```

打開 http://127.0.0.1:5173/ ，建立 session，再按 Start Recording。停止：先喺網頁按 Stop Recording，再喺終端按 Ctrl+C。`pnpm.cmd db:up` 下載及啟動 MongoDB，`pnpm.cmd db:stop` 停止（資料保留）。PowerShell 用 `pnpm.cmd`，因為執行原則可能封鎖 `pnpm.ps1`。

Key 只由 process environment 讀；`pnpm.cmd dev` 會將 repo root 嘅 `.env` 載入（已有嘅環境變數優先），唔會顯示內容。單一 Docker image、登入同 Cloud Run：見 [部署指南](../docs/cloud-run-deployment.md)。

## 設定

| 變數 | 預設 | 作用 |
| --- | --- | --- |
| `OPENROUTER_API_KEY` | — | ASR 同筆記。每月 key 上限剩唔夠 US$0.50 時，OpenRouter 會拒絕所有 audio 請求（HTTP 402） |
| `GOOGLE_AI_STUDIO_API_KEY` | — | Gemini（Vertex Express）：翻譯、術語、Ask、筆記 fallback |
| `JEV_API_KEY` | — | 術語排序 |
| `PLAYBACK_ASR_PROVIDER` | 有 OpenRouter key 就 `openrouter`，否則 `sensevoice` | ASR provider |
| `PLAYBACK_ASR_MODEL` | `qwen/qwen3-asr-1.7b` | 預設 ASR 模型；每個 session 可以喺 Transcript settings 揀 |
| `PLAYBACK_NOTE_MODEL` | `openrouter:qwen/qwen3.8-flash@none` | 筆記模型（`openrouter:<model>[@reasoning]` 或 `vertex:<model>`） |
| `PLAYBACK_GEMINI_MODEL` | `gemini-3.1-flash-lite` | Gemini 模型（只接受 3.1／3.5 Flash-Lite） |
| `PLAYBACK_AUTO_NOTES`／`_TERMS`／`_TRANSLATION` | 開 | 設 `no` 關閉對應嘅背景工作 |
| `PLAYBACK_ASR_REVIEW` | 開 | 逐 chunk 嘅第二遍 ASR；設 `no` 關閉 |
| `PLAYBACK_PAUSE_EXTERNAL_ASR` | 關 | 設 `yes` 暫停送 ASR，錄音照存 |
| `PLAYBACK_CONCEPT_GRAPH` | 關 | 設 `yes` 啟動 LightRAG sidecar，安裝見 [graph/README](graph/README.zh-HK.md) |
| `PLAYBACK_MONGO_URI`／`PLAYBACK_MONGO_DATABASE` | `mongodb://127.0.0.1:27017`／`playback_prototype` | 資料庫；測試用 `playback_e2e`；Firestore 用 `playback-prototype`（database ID 唔准底線） |
| `PLAYBACK_OFFLINE_TEST` | 關 | 設 `yes` 拒絕所有外部呼叫，API 只聽 127.0.0.1:5079 |

ASR 模型、語言提示同 fallback 次序：見 [ASR adapters](docs/asr-adapters.zh-HK.md)。

## 檢查

```powershell
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
npm.cmd --prefix app-temp/web run build
```

第一條係全部 offline checks（唔連網、唔用資料庫）。Browser checks、本機 MongoDB checks、要付費嘅 live checks 同 validation runner 嘅分類同指令見 [STRUCTURE.md 嘅 Checks](STRUCTURE.md#checks)。Live checks 一定要明確 opt-in，保存咗嘅結果會重用，唔會重複收費。

Week 3 測試素材喺 `app-temp/data/test-audio/sampleAudio/`：`sampleAudio.m4a`（首 20 分鐘）、`transcript.txt`（全堂機器字幕）、`Tutorial.txt`。

## 已知限制

- Linux container 只可以用 Browser 錄音；Browser 錄音只喺 offline fixture（Edge 假咪高峰、模擬螢幕分享）驗過，未經真 API／MongoDB、真分享系統聲或雲端驗收。兩種錄音都未做連續三小時實機測試。
- 廣東話逐字稿嘅繁體顯示用 Windows API（`LanguageSettings.CantoneseDisplay`），Linux 上會出錯，部署前要改。
- ASR 唔係原生 streaming：灰字預覽係短音檔請求，會增加費用。
- 自然課堂筆記質素、粵語準確度、三小時費用都未完整驗收；最新數字見 [POC 結果](../docs/poc-three-speed-notes-asr-context-2026-10-21.zh-HK.md)。
- 冇講者分離；教材只收純文字（PDF／PPT 抽取見 [研究](../docs/research/document-extraction.zh-HK.md)）。
- 登入係共用 demo 密碼，冇分享權限。
