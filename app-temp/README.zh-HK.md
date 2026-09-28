# Playback 本機 prototype

Code layout: [STRUCTURE.md](STRUCTURE.md).

2026-09-28 修復與驗收：最新行為以 [AI 實際流程](docs/ai-flow.zh-HK.md) 為準；真實 provider／實機測試、離線回歸、失敗及限制保存在本機 [validation 索引](data/validation/index.md)。下方 2026-09-26／27 的資料是歷史結果，不能代替本輪驗收。沒有原生串流 ASR 或三小時實機穩定性承諾。

每個 session 的 Transcript settings 現有三組獨立語言設定：ASR（Auto／混合、廣東話、普通話、英文）；Notes output（TC／SC／EN）；Translation target（廣東話繁體、TC、SC、EN，及原有日／韓語）。ASR 設定用於之後的辨識請求，已完成逐字稿不會重新上傳；廣東話模式以本機 Windows 字形轉換作繁體顯示，原始 ASR 文字保留。筆記設定用於下一次 AI revision，改語言後可按 Revise with AI；翻譯目標更改會重新排入翻譯佇列。設定保存到 session，重載後保留，不需清空資料庫。詳見 [ASR 語言設定](docs/asr-adapters.zh-HK.md#語言設定)。

2026-09-28：來源選單在 idle 時可選；舊 API 會明示重啟提示並阻止忽略所選來源的錄音。`dev` 亦檢查來源與 ASR adapter capability。筆記改用 presentation／lecture-note prompt，list、tree、diagram、table 和 code block 均按內容需要使用，不強制圖表，並要求移除舊 AI 筆記滲入的 `BASE VERSION` 標記。已保存版本不會自動改寫；下一次 Revise with AI 使用新 prompt。

ASR 現經 adapter 呼叫：有非範例的 `OPENROUTER_API_KEY` 時預設 OpenRouter `qwen/qwen3-asr-1.7b`，否則保留 SenseVoice；亦可明確設 `PLAYBACK_ASR_PROVIDER`。兩者目前是 REST：錄音最多約 8 秒封存，約每 2 秒嘗試更新灰字預覽，正式稿完成後替換。Streaming adapter 的 PCM 接線已具備，但尚未有具體 realtime provider。設定可按 session 切換 Whisper V3／Turbo，以及「廣東話 + English」自動辨識、繁體顯示。預覽增加音訊請求費用；三款模型的短音訊 live 比較、raw/display 差異及局限已保存於 validation。詳見 [ASR adapters 研究](docs/asr-adapters.zh-HK.md)。下方較早測試紀錄並非新流程的實測。

此目錄是 .NET 10 + React/Vite 的本機驗證版。網頁的錄音、暫停／繼續、停止控制同機運行的 .NET API；網頁重載不會停止 API 的收音。Electron 候選保留在 [`archive/desktop`](archive/desktop/README.archived.zh-HK.md)。**此版本沒有登入或分享權限，不可當作已可部署的產品。**

## 介面與錄音狀態

左側可建立 session 和 group、為 group 改名，以及把目前 session 移到 group。頂部顯示 Playback、目前 session 和錄音控制；窄螢幕用 Notes／Transcript 切換面板。偵測到音量活動時，Transcript 先顯示淡色待完成列；音訊保存後以原文或音訊狀態取代。列上顯示「Microphone」或「System audio」來源，目前沒有真人講者分離。連續而未轉錄的片段在同一小時內合成一列，原文優先顯示，音訊播放與來源細節預設收合。啟用翻譯後，完成的譯文顯示於原文下方，待處理譯文以淡色標示。筆記標題旁的 `vN` 是已儲存版本，底部固定 Save 和 Revise with AI。

**Activity** 已移至 Lecture notes 標題旁的 ↶。Popover 支援 hover／click／keyboard／Escape／touch，顯示本 session 最近 100 條真實執行、provider/model、queued/running/completed/failed/cache-hit、時間、耗時、來源及脫敏錯誤，並沿用保存的 note edits 和 Jev 決定。Restore this version 會新增還原版本。開啟紀錄不呼叫模型；舊版本沒有的執行資料不會補造。

時間線保留可收合的日期／小時，但取消多層邊線及逐列卡片，只使用輕微縮排。底部播放器固定兩行：第一行是播放模式、進度、時間與速度，第二行是後退五秒、播放／暫停及前進五秒。播放模式選單可切換 Full session 與音訊來源；這與頂部的錄音來源設定分開。

開始錄音前可選 **Microphone**（咪高峰）、**System audio**（Windows 預設播放裝置的全機 loopback）或 **Both sources**（兩者）。System audio 並非指定單一視窗。錄音期間模式鎖定，暫停／繼續沿用所選來源；要換模式須先停止。Both sources 如只有一個裝置可用仍會收音，並顯示另一個來源的實際錯誤。舊版 API 沒有來源選擇能力時，仍可選來源偏好，但 Record 會停用並顯示重啟提示；先停止錄音，再於自己的終端按 `Ctrl+C`、執行 `pnpm.cmd dev` 並重載網頁，才能使用新模式。現有 session 不需清空或遷移。

錄音狀態由本機 API 保持。頂部顯示本次 Record elapsed time，跨分段不重設，pause 凍結、resume 接續，重載由後端恢復；stop 後重新 Record 歸零。Silero VAD 用於人聲及送音策略，音量計另反映即時振幅。VAD 仍可能誤判；原始錄音保留供回聽，並非保證辨識所有人聲。

## 短期可行性審核（2026-09-27）

| 能力 | 結果 | 證據／限制 |
|---|---|---|
| .NET / 套件 | 已驗證可編譯 | SDK 10.0.400；Microsoft.Agents.AI 1.22.0、Google.GenAI 1.22.0、MongoDB.Driver 3.12.0。Agent Framework 透過 Google GenAI `IChatClient` 執行 agent。 |
| Gemini model ID | Vertex Express live 測試通過 | `gemini-3.5-flash-lite` 經 [Vertex Express API](https://docs.cloud.google.com/gemini-enterprise-agent-platform/reference/express-mode/api-reference) 和 .NET Agent Framework 產生含來源 ID 的合成逐字稿摘要與筆記。現有環境變數名稱仍是 `GOOGLE_AI_STUDIO_API_KEY`，但 key 實際用於 Vertex Express；project/location 在此路徑不參與請求。 |
| Google Search grounding | Vertex Express live 測試通過 | `generateContent` 配 `googleSearch` 回傳 FFT 公開來源連結與 search suggestions；後者在 Ask Playback 的沙盒 frame 顯示。[Vertex grounding 文件](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/grounding/grounding-with-google-search)。 |
| SenseVoice | 靜音合成 WAV live probe 通過 | `POST /stt/infer/upload` 的 `file` 欄位收到 HTTP 200；本機 API 亦成功保存一條標示不確定的空白 transcript。真實語音準確度及長時間負載未驗證。 |
| Jev | 合成術語 live 測試通過 | Playback 以 .NET HTTP 按 [TypeSafe OpenAPI](https://api.typesafe.ai/openapi.json) 的 Bearer 認證呼叫 `GET /v1/models` 及 `POST /v1/systemone`，不依賴 JS SDK。更新 key 後，`spectrogram` 返回排名、probability、confidence、usage，重複查詢命中本機快取；真實課堂術語品質未驗證。 |
| MongoDB | 本機合成資料整合檢查通過 | 2026-09-25 API 回報 `mongo:true`；session 隔離、note versions、chunk retry 與時間範圍已用合成資料實測。 |
| Windows system + mic | 已在本機保存音訊，可靠性尚未驗證 | 使用者已成功播放保存的錄音；咪高峰與 system loopback 分別嘗試啟動，任何一個可用便繼續錄音並顯示另一個的錯誤。本機瀏覽器檢查已驗證咪高峰回報 Core Audio `0x80070057` 時可用 system loopback 開始、暫停、繼續和停止。長時間及不同裝置尚未測試。[NAudio WASAPI 文件](https://github.com/naudio/NAudio/blob/main/Docs/WasapiRecorder.md)；[Microsoft loopback 說明](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)。 |

## 架構

```text
React/Vite UI ── localhost API ──> MongoDB: sessions/materials/chunks/transcripts/notes
                              ├── Agent Framework + Vertex Gemini 3.5 Flash-Lite: notes, translation, private Q&A
                              ├── Vertex Gemini 3.5 Flash-Lite + Google Search: explanations / web evidence
                              └── Jev: candidate term decisions
Windows mic + system output ── .NET API process ── 最多約 8s WAV ── local disk + MongoDB
                                                    └── ASR adapter: SenseVoice / OpenRouter Qwen (automatic queue, at most 2 requests)
```

錄音先在 `app-temp/data/local-capture` 完成每段 WAV，再匯入 `app-temp/data/audio`；匯入失敗的完整 WAV 會留待下一次 Start 時重試。MongoDB 只存 metadata。原始 ASR 為獨立欄位，翻譯及修訂不覆蓋它。材料與 transcript 查詢依 session ID 隔離。私人問題只留在當前 UI 狀態，暫未持久化。

## 執行

`compose.yaml` 提供只綁定 localhost 的 [官方 Mongo image](https://hub.docker.com/_/mongo/)。開啟 Docker Desktop 後，在 repo root 執行：

```powershell
pnpm.cmd dev
```

`pnpm.cmd dev` 首次會自動安裝缺少的 web 套件及還原 .NET 套件（可能連網），不必先執行 `setup`。啟動時檢查 MongoDB；若未連上且沒有指定 `PLAYBACK_MONGO_URI`，會提示並嘗試以現有本機 image 啟動 Playback 的 MongoDB container，然後等待資料庫就緒。此步不會下載 image 或重建現有 container。若 Docker Desktop 未開或本機沒有 image，網頁仍會起動，但不能建立或讀取 session；先開 Docker Desktop，再明確執行 `pnpm.cmd db:up`（首次可能下載 image）。若用 process environment 或 `.env` 的 `PLAYBACK_MONGO_URI` 指定其他 MongoDB，`dev` 不會啟動 bundled container；不要輸出或提交連接字串。

開始錄音後，保存好的非靜音音訊會自動送往目前配置的 ASR provider。啟動時會掃描舊的待處理片段；失敗時在背景按退避間隔重試，Transcript 時間線直接顯示音訊、狀態和錯誤。每段 WAV 先保存；送往 ASR 前統一轉成 16 kHz 單聲道 PCM，以減少系統聲的上傳大小，原始 WAV 保留作播放。只有完全數位靜音才跳過 ASR；即時錄音提示用本機 Silero 模型判斷語音，與後續 ASR 分開。靜音或 ASR 沒有回傳文字的片段在每個小時合併為一條可展開的「No audio」分隔列，展開後可檢查每段來源、播放及重試。逐字稿按日期、小時及每筆 `HH:mm:ss` 時間分層顯示；ASR 回應中的語言／情緒控制 token 不顯示為逐字稿。Gemini 有配置時，背景程序每兩分鐘合併新逐字稿與教材修訂 rolling notes，避免每個 30 秒音訊片段各自呼叫模型；手動「Revise with AI」仍即時執行。如需暫停背景筆記，可在 process environment 設 `PLAYBACK_AUTO_NOTES=no`。

`dev` 在同一個終端啟動 API 與 Vite；不會自行錄音。瀏覽器開 `http://127.0.0.1:5173/`。若兩個新版服務已在運行，再執行 `dev` 會檢查資料庫並提示開網頁；若只運行其中一個或 API 是舊版，先在原來終端按 `Ctrl+C`，再執行 `pnpm.cmd dev`。錄音時先按網頁的 Stop Recording，再於終端按 `Ctrl+C` 停止兩個程式。要停止 MongoDB，可執行 `pnpm.cmd db:stop`，資料 volume 會保留。Windows PowerShell 使用 `pnpm.cmd`，因為本機執行原則可能封鎖 `pnpm.ps1`。

先建立 session，再在 Transcript 頁按 Start Recording。API 會嘗試開啟預設咪高峰與預設播放裝置的 loopback；任何一個可用便繼續，兩者分開保存。一般錄音每段最多約八秒，滿三秒且語音結束時可提早完成 WAV，暫停及停止時會封存剩餘片段。咪高峰與系統聲以不同講者來源顯示；點逐字稿列只播該列的音訊，底部唯一播放器的「Full session」則連播整段，並可切換同步混合／只播咪高峰／只播系統聲、拖曳時間及調整速度。整個工作區固定於視窗高度，筆記與 Transcript 各自捲動，Save 保持在筆記底部可見。收音發生在**運行 API 的那部 Windows 電腦**，遠端 API 無法錄到你電腦的聲音。此功能尚未完成連續三小時實機錄音驗證；若 API 被強制終止，最後尚未封口的 `.wav.part` 需要人工檢查。

錄音音訊會自動上傳目前配置的 ASR provider。介面與 API 不設同意勾選或同意 header；AI 筆記、提問和翻譯直接觸發 Gemini，術語評估直接觸發 Jev。憑證只從 process environment 讀 `GOOGLE_AI_STUDIO_API_KEY`、`OPENROUTER_API_KEY`、`JEV_API_KEY`；沒有 key 時顯示實際錯誤，不會回傳假成功。

若執行環境暫時禁止外部音訊傳送，可設定 process environment `PLAYBACK_PAUSE_EXTERNAL_ASR=yes` 再啟動 API。這只暫停 ASR 佇列，錄音仍保存在本機；Transcript 顯示暫停狀態。移除此設定並重啟後，待處理音訊會自動續傳。

2026-09-26 用合成音訊量度 SenseVoice：0.25 秒靜音的端到端時間 1.08 秒；10 秒單音首次 10.04 秒、暖機後 3.98 秒；兩個同時上傳各為 5.15／5.96 秒，服務回報 inference 由單次 1.54 秒升到約 2.35／2.39 秒。回應包含 `duration_seconds`、`file`、`inference_time_seconds`、`language`、`raw`、`rtf`；`raw` 會包含 `<|en|>` 等控制 token。網絡及服務排隊時間佔了可見延遲；服務 CPU 配置無法由回應確認。這些數字不能代表真實演講語音。背景 ASR 暫設最多兩個並行；增加並行未證明可改善單段延遲。靜音檢查只能節省完全靜音片段的上傳；即時語音提示由另一本機 VAD 處理。

## 本次測試與確切限制

- `dotnet build app-temp/api/Playback.Api.csproj`、`npm.cmd run build`（web、當時的 desktop 候選）已通過；本機 WASAPI capture 尚未做實機測試。
- 獨立測試 API 端口的 `/api/capture/status` 回報 `idle`、無效 session 的 Start 回 HTTP 409、閒置 Stop 安全返回；沒有在測試中開咪。
- `dotnet run --project app-temp/checks/Playback.Checks.csproj`：驗證 ASR 原文優先、靜音與未知 provider schema、web citation 映射與不安全 URL 拒絕。
- 真實 Gemini 檢查使用 `pnpm.cmd test:gemini-live`，從程序環境或根目錄 `.env` 載入 `GOOGLE_AI_STUDIO_API_KEY`。2026-09-27 以虛構 Fourier/FFT 逐字稿通過含來源 ID 的 Markdown 筆記、Google Search 公開 citation 與 suggestions、兩段批次翻譯 ID 核對，以及經實際來源搜尋及引用核對的問答。這是合成檢查，沒有驗證 Week 3 內容品質或資料庫中的翻譯版本。`pnpm.cmd dev` 也會載入可選的根目錄 `.env`，程序環境已有的值優先；不會輸出或提交密鑰。Vertex Express key 請求不使用 `GOOGLE_AI_STUDIO_PROJECT_ID` 或 `GOOGLE_AI_STUDIO_LOCATION`。
- [Vertex Express REST](https://docs.cloud.google.com/gemini-enterprise-agent-platform/reference/express-mode/api-reference) 目前沒有顯式 context cache 端點；[隱式 cache](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/context-cache/context-cache-overview) 由 Google 自動處理。程式用 transcript ID/輸入 hash 避免重做筆記，用有上限的來源片段組 prompt，Jev 模型發現及同一術語排名在本機短期快取；沒有聲稱量到實際 cache hit 或費用。
- `node app-temp/archive/desktop/queue-check.mjs`：封存候選曾用合成 1,080 段、每段十秒的三小時時間線測試 queue。這**沒有證實**真實 48 kHz 連續三小時擷取、權限、CPU／磁碟吞吐或系統音源。
- `node app-temp/web/src/test/browser-check.mjs`：在本機 Edge 測等寬雙欄、主題、Markdown 的**有標籤實際 SVG 圖表**、Ask Playback、手機 tabs、Start Recording 按鈕，無頁面錯誤；測試沒有開咪。
- `node app-temp/dev-check.mjs`：不啟動 container，以替身函式測現有 DB、自訂連接字串、啟動及等待、Docker 失敗訊息。
- `node app-temp/web/src/test/asr-ui-check.mjs`：以攔截 API 測音訊時間線、靜音收合、不同螢幕寬度與獨立捲動、session 翻譯設定及本機錄音請求；不會啟動咪高峰或上傳音訊。
- SenseVoice 靜音 fixture 在 2026-09-26 直接呼叫回 HTTP 200；此前版本的本機 API 單段 retry 曾保存一條空白但標示不確定的 transcript。新版本跳過完全數位靜音，並已用短篇真實語音完成端到端測試。Gemini 的合成筆記、公開搜尋、批次翻譯和問答已 live 通過。Jev 在更新 key 後於 2026-09-27 通過合成術語的模型發現、排名及快取測試；`POST /api/terms/evaluate-synthetic` 的四詞 API 路徑仍未 live 測試。
- 本機收音的硬件權限、Windows loopback 無聲情況、較長真實語音及連續三小時穩定性仍需實測。流暢的即時筆記取決於 chunk 完成及 ASR latency；不是 streaming ASR。
- `node app-temp/api/integration-check.mjs` 已通過 session 隔離、note versions、chunk retry 與時間範圍測試；測試建立兩個合成 session，並在結束時刪除本次資料。
- 目前只可附上文字教材。PDF 頁碼擷取、講者辨識、長時間磁碟配額、分享權限及部署未實作。

## 相關官方資料

- [Microsoft Agent Framework 的 Google Gemini 整合](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)
- [Google Search grounding 與 annotation](https://ai.google.dev/gemini-api/docs/google-search)
- [Electron desktopCapturer](https://www.electronjs.org/docs/latest/api/desktop-capturer)
- [TypeSafe OpenAPI](https://api.typesafe.ai/docs)

## 本機端到端測試

> 以下為舊版測試紀錄；目前的離線指令與限制見下方「2026-09-26：session、逐字稿與 Notes 流程」。

先用 `pnpm.cmd dev` 啟動 API、Vite 和本機 MongoDB，然後在另一個 PowerShell 視窗執行：

```powershell
pnpm.cmd test:e2e
```

測試使用本機 Edge 和真實 UI/API/MongoDB：在 UI 建立獨立 demo session 與 group、附加文字教材、儲存筆記並重載驗證，再上傳一秒數位靜音 WAV，確認背景佇列自動標記為 `silent`、沒有產生假逐字稿。預設測試離線且不會把音訊送往 SenseVoice；測試結束時只刪除本次建立的 `E2E demo` session、group 和 WAV。改動 UI、API、儲存或 ASR 流程後，應重新執行此測試。

如已有完成轉寫的 session，可額外檢查真實逐字稿及日期／小時時間樹：

```powershell
$env:PLAYBACK_E2E_REAL_SESSION_ID = '<session-id>'
pnpm.cmd test:e2e
Remove-Item Env:PLAYBACK_E2E_REAL_SESSION_ID
```

2026-09-26 使用獲授權的 5.1 秒粵語 WAV 與 MongoDB 驗證：SenseVoice 直接請求約 2.28 秒，回報推理約 0.94 秒；新上傳到 API 約 17 毫秒獲接受，約 4.61 秒後 MongoDB 出現逐字稿。API 儲存的逐字稿有文字而沒有 SenseVoice 標記，UI 的時間樹亦通過 Playwright 檢查。這些是短片段的單次實測，未有人工標準稿比對文字準確度，也不能代表三小時錄音的吞吐量。

## 2026-09-26：session、逐字稿與 Notes 流程

- Session 只有一個可空的 `groupId`。側欄以 group 為父層顯示 session，可在 group 內建立、重新命名 group，並在目前 session 旁移動至其他 group 或「未分組」。現有 MongoDB 紀錄會按新增欄位的預設值讀取，不需要清空資料庫。
- Transcript 標題列的齒輪提供整個 session 的「啟用翻譯」與目標語言；翻譯工作每 15 秒檢查待處理原文，按 session 將最多 10 段合成一次 Gemini 請求，並按 transcript ID 驗證及逐段儲存譯文版本。失敗會保存錯誤、次數及下一次重試時間；可手動重試。關閉只隱藏譯文，原文不改。選取原文可帶 transcript ID、時間與選取文字至 Ask Playback。
- ASR 分開 `silent`（完全數位靜音）、`asr-empty`（服務沒有回傳文字）、`asr-error`（辨識失敗）、`transcribed`，音訊仍與 chunk 關聯。空文字不代表低 confidence 或人工覆核。辨識原文及材料中有來源的術語以本地保守規則標籤；Ask Playback 使用 session 內的來源 ID，網絡搜尋要逐次勾選。
- Notes 按 transcript 保存 `pending`／`processing`／`failed`／`completed`、嘗試次數及重試時間。每次最多處理 40 條；遲到紀錄不依賴時間游標。版本保存實際輸入的 transcript/material ID、輸入 SHA-256、來源時間與先前筆記版本。使用者編輯另成版本，AI 失敗不覆寫它；失敗可在「Revise with AI」重試。來源清單可跳到音訊時間或材料。
- 錄音一開始便自動處理封存好的音訊，介面與 API 沒有 session 同意勾選或同意 header。舊版留下的 `awaiting-consent` 音訊在新版 API 啟動後重新排入 ASR。`PLAYBACK_OFFLINE_TEST=yes` 時服務只綁定 `127.0.0.1:5079`、停用背景掃描，provider 呼叫一律拒絕；測試前端使用 5174。測試 fixture 端點只接受 `E2E demo` session 與合成文字，測試腳本會刪除其建立的 session、group 與音訊。

離線檢查：`dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/`。在已啟動本機 MongoDB 後，用 `dotnet build app-temp/api/Playback.Api.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/` 編譯；另一個 PowerShell 設定 `$env:PLAYBACK_OFFLINE_TEST="yes"; $env:ASPNETCORE_ENVIRONMENT="Development"` 後執行 `dotnet app-temp/api/bin/verification/net10.0/Playback.Api.dll`，再在 `app-temp/web` 的 PowerShell 設定 `$env:PLAYBACK_OFFLINE_TEST="yes"` 並執行 `npm.cmd run dev -- --port 5174`。測試 shell 同樣設定 `PLAYBACK_OFFLINE_TEST=yes`，執行 `node app-temp/api/integration-check.mjs` 及 `node app-temp/web/src/test/e2e-check.mjs`。不要為測試設定講課資料、對外 provider key，亦不要啟動容器或上傳音訊。實機咪高峰錄音仍依賴 Windows 音訊裝置／權限；若 Core Audio 拒絕啟動，UI 顯示實際錯誤並保持 idle，不能視作錄音成功。

## 2026-09-27：Week 3 唯讀長時段檢查

`python app-temp/checks/long-lecture-check.py "app-temp\data\test-audio\sampleAudio"` 只讀取 repo 內的 Week 3 素材 `sampleAudio.m4a` MP4 header、`transcript.txt` 與 `Tutorial.txt`。音訊為首 1,200.01 秒（14,607,746 bytes）；完整逐字稿有 1,266 段、97,717 字元，延伸至約 2:44:49，最後一句無時間戳，離線檢查暫估 14 秒。20 分鐘後的逐字稿沒有保留的對應音訊；長時段來源檢查只驗證文字及時間資料。此檢查不解碼或上傳音訊。

依每兩分鐘更新及每次最多 40 段模擬，該逐字稿會有 82 次定時筆記請求；原本每次重送 3,000 字元教材時，來源輸入約 432,337 字元，其中 243,000 字元是第二次起重複的教材。自動筆記現在只在初次或新增教材時發送未處理教材；估算來源輸入降至約 189,337 字元。這些數字不含前版筆記、指示詞、模型輸出或 token 計費，也不是已發生的外部呼叫。每次實際請求現會記錄段落數與 UTF-8 輸入大小，不記錄原文。

`dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/ -- --sample-audio "app-temp\data\test-audio\sampleAudio"` 用真實逐字稿在記憶體驗證 40 段 backlog、無重複來源 ID、開頭／中段／末段問答的來源挑選及逐字稿時間。若整批離線處理，需要 32 個最多 40 段的 batch；這與上面 82 次定時更新是兩種不同情境。另以 360 段合成三小時時間線測早／晚來源問答；web fixture 以 1,266 段跨約三小時驗證視窗高度、面板捲動、Save 可見、精簡逐字稿及無聲收合。這些檢查不代表真實三小時 ASR、筆記內容品質或課堂問答準確度已通過。

離線 API 整合測試可在獨立的 5079 端口執行，設定 `PLAYBACK_OFFLINE_TEST=yes`、`PLAYBACK_PAUSE_EXTERNAL_ASR=yes`、`PLAYBACK_AUTO_NOTES=no`，再執行 `node app-temp/api/integration-check.mjs`。此模式會確認新合成音訊維持待處理，不會因背景佇列送往 SenseVoice；測試僅清理自己建立的合成 session、group 與音訊。測試前先查核現有 5078 API 程序，勿重啟舊程序觸發舊資料重試。

翻譯若啟用，1,266 段在沒有失敗重試時約需 127 次十段批次請求，原逐段流程會有 1,266 次。批次回應必須包含每個 transcript ID 且不可重複；解析失敗會標記該批次失敗並按原有退避規則重試。Jev 的 `Bearer` header、`GET /v1/models` 及回應 schema 已按 [TypeSafe OpenAPI](https://api.typesafe.ai/openapi.json) 核對。2026-09-27 更新 key 後，`pnpm.cmd test:jev-live` 以虛構 `spectrogram` 完成模型發現、排名及快取：`high`、probability `0.84`、confidence `0.53`，並收到 usage 物件；這不代表真實課堂術語品質已驗證。[Vertex Express REST 資源](https://docs.cloud.google.com/gemini-enterprise-agent-platform/reference/express-mode/api-reference) 仍沒有建立顯式 context cache 的端點，故此流程沒有使用顯式 cache。

問答現在按問題詞語選取最多五份教材，中文連續字詞會拆成相鄰雙字供長課堂搜尋；長教材截取含匹配詞的最多 3,000 字，逐字稿每段及選中來源各截取最多 1,600 字。模型回答必須以方括號引用本次提供的來源 ID；引用失敗會拒絕 lecture answer，但已允許的獨立 web 分支仍會執行；原有的 `[unclear]` 等 ASR 標記不作引用。evidence 只列實際引用的來源及其音訊時間或教材名稱。這能防止缺失或虛構 ID 被當成來源，但不能證明引用內容的語義正確。離線協定檢查、Week 3 開頭／中段／末段唯讀搜尋、合成中文三小時搜尋，以及虛構資料 Gemini live 問答均已通過。

Week 3 音訊的 live 檢查會把測試片段送到 SenseVoice，並把辨識出的部分文字送到 Gemini 驗證問答及筆記；測試程式不在終端輸出音訊、逐字稿或金鑰。

## 2026-09-27：筆記編輯紀錄與補充解釋

每個新筆記版本保存與前一版本逐行比較的新增／移除紀錄、行號及該行實際引用的 transcript/material ID；agent 版本另存本輪納入的 transcript/material ID。`GET /api/sessions/{id}/notes/edits` 返回最近 100 個版本的編輯紀錄；筆記預覽可展開最新版本的變更。長篇大量改動時，後端以整段移除／新增紀錄代替高成本逐行配對。若 AI 生成時有人手版本先儲存，舊基底的 AI 結果會被拒絕並待下次重試；無效 `[ref:ID]` 同樣拒絕。這是可審核的文字差異與輸入清單，不能證明 AI 對每一個未引用的句子有正確歸因。

2026-09-28 更新：來源事件延遲約四秒排入 Jev，每次最多三個候選詞；自動模式預設開啟（`PLAYBACK_AUTO_TERMS=no` 可關）。「Review next key terms」保留為手動重試入口。候選包含小寫／中文並攜帶 context，門檻仍為 probability ≥ .75、high/medium、confidence ≥ .50。長按／鍵盤可展開高亮詞的保存解釋。門檻的自然課堂 precision/recall 未評估。

Jev 選中後立即以 Gemini + Google Search 解釋，短文標示「AI／網絡補充」加入筆記及 `[ref:ID]`，詳解／HTTPS evidence 保存於 `term_insights`；不是等點開才生成。按 term/context/output language/version 保存、去重和重用。搜尋失敗顯示真實錯誤，普通筆記仍可處理。2026-09-28 已以合成樣本 system loopback 驗證自動來源事件、Jev、搜尋、note version 及 UI；自然課堂品質仍未驗收。

## 2026-09-27：系統聲 ASR 與共用播放器複核

現場 `playback_e2e` 的 `Test` session 在檢查時仍正在錄音。當時 419 個 chunk 中有 38 個成功轉錄、231 個 ASR 無字、48 個停止自動重試、100 個等待重試、2 個正在處理；失敗 148 個全屬系統聲，錯誤同為 `Error while copying content to a stream.`。系統聲原檔為 48 kHz 雙聲道 float WAV，咪高峰為 16 kHz 單聲道 float WAV。ASR 現在只在上傳前把兩種來源統一轉成 16 kHz 單聲道 PCM；播放仍讀原始 WAV。合成 48 kHz 雙聲道轉碼的格式、音量及原檔保留檢查通過；一段合成 30 秒雙聲道音訊亦成功到達 SenseVoice，回傳空字而非上傳中斷。這尚不能證明現場失敗的系統音訊重試必定成功。

另從現場 WAV 逐組抽樣：咪高峰成功轉錄的 12 段平均 RMS 0.0093，ASR 無字的 12 段平均 RMS 0.0051，兩組峰值範圍有重疊；一段成功轉錄的峰值僅 0.0124。故沒有用固定音量門檻將低聲音訊直接丟棄。畫面上的即時活動提示是能量檢測，不是可保證辨識人聲的 VAD；近乎無字的音訊仍可能進 ASR，之後在「No audio」分隔列收合。

底部播放器只有一個 `<audio>`。逐字稿列只播該列；「Full session」按 30 秒窗口連播同一 session，可選咪高峰、系統聲或同步混合，亦可拖曳、跳五秒及改速度。測試 API 的真實 WAV 與合成重疊聲道通過取樣值核對，Playwright 使用真實 API/MongoDB 驗證整段混音及切換來源；桌面與 320–1024 像素介面 fixture 均通過。真實長時間連播及現場系統聲重試仍待新 API 啟動後驗證；目前 5078 正在錄音，未被測試重啟。

若瀏覽器已載入新版 UI 而 5078 仍是舊 API，底部播放器會顯示「Restart API for session audio」並停用整段播放；逐字稿單列仍可播放。`pnpm.cmd dev` 亦會識別舊 API 並提示重啟，避免把未提供的混音端點當成可用。

## 2026-09-27：錄音狀態與逐字稿時間線

錄音時頂部顯示由本機擷取樣本計算的最近 10 秒音量波形（每 0.5 秒更新）。錄音狀態每 0.5 秒檢查一次；任一來源首次通過聲音能量檢測，Transcript 就在本地日期 → 小時 → 每筆 `HH:mm:ss` 樹內加入半透明的未完成音訊列，不必等約 30 秒 WAV 輪轉。WAV 完成並保存後立即刷新為可播放列。未檢出聲音時只顯示「Listening for sound」；此能量檢測不是可靠的語音分類。列上的時間是本地時鐘，而非從 00:00 開始的錄音位移。

SenseVoice 網絡錯誤會保留錄音，最多自動嘗試三次，之後標成 `asr-manual` 並停止背景重試。舊版沒有重試次數的 `asr-error` 在正常 API 啟動時轉成手動重試，保留音訊與原錯誤，不會重新排入大量背景請求。連續失敗段落合併顯示，使用者可按「Retry ASR」重試同組已保存音訊。`asr-empty`（有音訊但沒有辨識到字）及真正數碼靜音均收進每小時一條「No audio」分隔列；展開後會區分 ASR 無字、來源、播放與重試。咪高峰及系統聲音獨立處理，所以同一時段可能一邊成功、另一邊無字或失敗。Ask Playback 的輸入來源現以 `[source-id]` 明確標示引用格式；如 AI 首次回覆的引用不合格，會要求重寫一次並重新驗證，仍不合格則顯示真實錯誤，不產生假引用。未設定 Vertex key 時，聊天視窗保留問題並顯示缺少憑證的錯誤。

離線驗證已通過 .NET 協定檢查、web build、Playwright UI fixture、真實 API/MongoDB 整合檢查及合成 WAV 的瀏覽器端到端檢查。`--sample-audio-preview "app-temp\data\test-audio\sampleAudio"` 會唯讀解碼首 20 分鐘為 40 段 30 秒、16 kHz mono WAV（記憶體內處理，不保存或上傳），40 段均驗出音量。真實課堂首 20 分鐘的 SenseVoice／Gemini live 結果見下文；provider 費用並未量測。

端到端測試可用 process environment 的 `PLAYBACK_MONGO_DATABASE=playback_e2e` 指向獨立測試資料庫；只接受 `playback_prototype` 或 `playback_e2e`，health endpoint 會回報目前名稱。離線 API/MongoDB 測試已在此獨立資料庫再通過一次，測試會刪除自己建立的 `E2E demo` session、group 和音訊，原有 `playback_prototype` 不受影響。啟動 live API 前應先查 health 的 `database`，以免背景佇列處理既有 session 的錄音。

`--sample-audio-live "app-temp\data\test-audio\sampleAudio"` 是真實 provider 端到端檢查：把保留的 20 分鐘音訊加速送成 40 個本機 API WAV chunk，等 SenseVoice 返回，再以一段已辨識來源驗證 Ask Playback 引用與 Gemini 筆記，最後清理自己建立的 test session／音訊。API health 須顯示 `database=playback_e2e`、Gemini／ASR 可用、`autoNotes=false`、`autoTerms=false`，否則檢查拒絕開始。程式可從根目錄 `.env` 載入金鑰到程序環境，代理不讀取或顯示檔案內容。2026-09-27 以保留的單一 `sampleAudio.m4a` 測試：33 段成功轉錄、6 段 provider 回傳無文字、1 段因 provider 提早斷線三次而轉為手動處理；Ask Playback 回答有來源引用，Gemini 筆記成功保存。測試 session、MongoDB 紀錄與 API 音訊已清理。

先在 repo 根目錄執行 `dotnet build app-temp/checks/Playback.Checks.csproj`，再執行 `npm.cmd run test:sample-audio-live`。後者會自動載入 `.env` 到子程序、用已編譯 DLL 啟動只連接 `playback_e2e` 的本機 API、執行 sample audio 檢查，再停止該測試 API。若 5078 端口已有 API，腳本會拒絕開始，避免誤用原有 session。

即時聲音提示現在使用本機 Silero VAD 模型（`api/Resources/silero_vad.onnx`，MIT 授權見同目錄）：偵測到語音便顯示半透明錄音列，持續約一秒後才啟動聲波及呼吸效果。錄音欄的倒數顯示到下一段 30 秒音訊封存。網頁每 250 ms 讀取 capture status；每秒更新倒數及時間範圍。`dotnet app-temp/checks/bin/verification/net10.0/Playback.Checks.dll --vad-sample app-temp/data/test-audio/sampleAudio` 會在本機檢查 sampleAudio 首 30 秒及數碼靜音，不上傳音訊。

`npm.cmd run test:asr-live` 會從 `.env` 載入程序環境，產生 30 秒合成音調並真正呼叫 SenseVoice HTTPS REST 端點，檢查回應的時長及推理數據；會有 provider 費用。這項檢查要在正常網絡權限下執行。作業系統拒絕 HTTPS 連線時，後端會立即把音訊保存並標記供手動重試，不會再作無效的自動重試。若 AI 筆記可用逐字稿少於 40 字元，畫面會顯示等待更多語音，暫不發出無內容的 Gemini 筆記請求。

2026-09-28 再用 `sampleAudio` 首 20 分鐘作隔離的真實 API／provider 測試：40 段中 32 段成功轉錄、7 段無字、1 段因 provider 三次提早終止回應而保留為手動重試；Ask Playback 回答有來源引用，Gemini 筆記成功保存，測試 session 已清理。VAD 離線測試以同一音訊首 30 秒檢查 8／16／48 kHz 及數碼靜音，首次偵測語音在 200 ms。這些檢查不等同於實機 WASAPI 咪高峰和系統聲連續收音驗收。
