# Playback 本機 prototype

此目錄是 .NET 10 + React/Vite 的本機驗證版。網頁的 Start／Stop 控制同機運行的 .NET API 錄音；網頁重載不會停止 API 的收音。Electron 候選保留在 [`archive/desktop`](archive/desktop/README.archived.zh-HK.md)。**此版本沒有登入或分享權限，不可當作已可部署的產品。**

## 短期可行性審核（2026-09-25）

| 能力 | 結果 | 證據／限制 |
|---|---|---|
| .NET / 套件 | 已驗證可編譯 | SDK 10.0.400；Microsoft.Agents.AI 1.22.0、Google.GenAI 1.22.0、MongoDB.Driver 3.12.0。Agent Framework 透過 Google GenAI `IChatClient` 執行 agent。 |
| Gemini model ID | 官方文件確認 | `gemini-3.8-flash`、`gemini-3.5-flash-lite` 均在 [官方模型表](https://ai.google.dev/gemini-api/docs/models)。未有 process key，因此未做 live inference 或成本測量。AI Studio 使用 `vertexAI: false`；Vertex AI 是另一套部署／認證設定。 |
| Google Search grounding | 官方文件確認，實際呼叫未驗證 | 獨立 REST `/v1beta/interactions` call 使用 `google_search`，保留 URL annotation 的起止索引。[Google 文件](https://ai.google.dev/gemini-api/docs/google-search)；[Agent Framework Gemini 文件](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini) 的 C# 例子使用一般 chat client，Web Search 例子是 Python。 |
| SenseVoice | 靜音合成 WAV live probe 通過 | `POST /stt/infer/upload` 的 `file` 欄位收到 HTTP 200；本機 API 亦成功保存一條標示不確定的空白 transcript。真實語音準確度及長時間負載未驗證。 |
| Jev | 官方 schema 已核對，授權受阻 | [TypeSafe OpenAPI](https://api.typesafe.ai/docs) 要求先 `GET /v1/models`，再用 `model/state/questions` 呼叫 `POST /v1/systemone`。process 缺 `JEV_API_KEY`，沒有 live ranking、confidence 或 usage 測量。 |
| MongoDB | 本機合成資料整合檢查通過 | 2026-09-25 API 回報 `mongo:true`；session 隔離、note versions、chunk retry 與時間範圍已用合成資料實測。 |
| Windows system + mic | 已在本機保存音訊，可靠性尚未驗證 | 使用者已成功播放保存的錄音；咪高峰必須成功開始，system loopback 若失敗會顯示原因而保留咪高峰錄音。兩個來源獨立保存，長時間及不同裝置尚未測試。[NAudio WASAPI 文件](https://github.com/naudio/NAudio/blob/main/Docs/WasapiRecorder.md)；[Microsoft loopback 說明](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)。 |

## 架構

```text
React/Vite UI ── localhost API ──> MongoDB: sessions/materials/chunks/transcripts/notes
                              ├── Agent Framework + Gemini 3.8 Flash: notes and private Q&A
                              ├── Gemini 3.5 Flash-Lite + Google Search: explanations / web evidence
                              └── Jev: candidate term decisions
Windows mic + system output ── .NET API process ── 10s WAV ── local disk + MongoDB
                                                    └── SenseVoice (automatic queue, at most 2 requests)
```

錄音先在 `app-temp/data/local-capture` 完成每段 WAV，再匯入 `app-temp/data/audio`；匯入失敗的完整 WAV 會留待下一次 Start 時重試。MongoDB 只存 metadata。原始 ASR 為獨立欄位，翻譯及修訂不覆蓋它。材料與 transcript 查詢依 session ID 隔離。私人問題只留在當前 UI 狀態，暫未持久化。

## 執行

`compose.yaml` 提供只綁定 localhost 的 [官方 Mongo image](https://hub.docker.com/_/mongo/)。開啟 Docker Desktop 後，在 repo root 執行：

```powershell
pnpm.cmd dev
```

`pnpm.cmd dev` 首次會自動安裝缺少的 web 套件及還原 .NET 套件（可能連網），不必先執行 `setup`。啟動時檢查 MongoDB；若未連上且沒有指定 `PLAYBACK_MONGO_URI`，會提示並嘗試以現有本機 image 啟動 Playback 的 MongoDB container，然後等待資料庫就緒。此步不會下載 image 或重建現有 container。若 Docker Desktop 未開或本機沒有 image，網頁仍會起動，但不能建立或讀取 session；先開 Docker Desktop，再明確執行 `pnpm.cmd db:up`（首次可能下載 image）。若用 process environment `PLAYBACK_MONGO_URI` 指定其他 MongoDB，`dev` 不會啟動 bundled container；不要把連接字串放進 `.env` 或提交。

開始錄音就代表把保存好的非靜音音訊自動送往公司 SenseVoice，毋須逐段同意或 Retry。啟動時會掃描舊的待處理片段；失敗時在背景按退避間隔重試，Sources 顯示狀態和錯誤。每段 WAV 先保存，只有完全數位靜音才跳過上傳；這個簡單檢查不是可靠的語音 VAD。逐字稿按日期、時段及音訊時間分層顯示；ASR 回應中的語言／情緒控制 token 不顯示為逐字稿。若要由背景自動把逐字稿及教材送 Gemini 修訂 rolling notes，另在 process environment 設 `PLAYBACK_AUTO_NOTES=yes`；預設只由 UI 明確要求 Gemini 功能。

`dev` 在同一個終端啟動 API 與 Vite；不會自行錄音。瀏覽器開 `http://127.0.0.1:5173/`。若兩個新版服務已在運行，再執行 `dev` 會檢查資料庫並提示開網頁；若只運行其中一個或 API 是舊版，先在原來終端按 `Ctrl+C`，再執行 `pnpm.cmd dev`。錄音時先按網頁的 Stop Recording，再於終端按 `Ctrl+C` 停止兩個程式。要停止 MongoDB，可執行 `pnpm.cmd db:stop`，資料 volume 會保留。Windows PowerShell 使用 `pnpm.cmd`，因為本機執行原則可能封鎖 `pnpm.ps1`。

先建立 session，再在 Transcript 頁按 Start Recording。API 會先開預設咪高峰，再嘗試錄預設播放裝置的聲音；兩者分開保存。每十秒完成一段 WAV，Stop 後在 Sources 查看和播放。收音發生在**運行 API 的那部 Windows 電腦**，遠端 API 無法錄到你電腦的聲音。此功能尚未完成實機權限、靜音 stream 或連續三小時驗證；若 API 被強制終止，最後尚未封口的 `.wav.part` 需要人工檢查。

錄音音訊會自動上傳 SenseVoice。介面與 API 不設同意勾選或同意 header；Gemini 與 Jev 功能由使用者在 UI 直接觸發，各自只從 process environment 讀 `GOOGLE_AI_STUDIO_API_KEY`、`JEV_API_KEY`。沒有 key 時顯示 unavailable；不會回傳假成功。

2026-09-26 用合成音訊量度 SenseVoice：0.25 秒靜音的端到端時間 1.08 秒；10 秒單音首次 10.04 秒、暖機後 3.98 秒；兩個同時上傳各為 5.15／5.96 秒，服務回報 inference 由單次 1.54 秒升到約 2.35／2.39 秒。回應包含 `duration_seconds`、`file`、`inference_time_seconds`、`language`、`raw`、`rtf`；`raw` 會包含 `<|en|>` 等控制 token。網絡及服務排隊時間佔了可見延遲；服務 CPU 配置無法由回應確認。這些數字不能代表真實演講語音。背景 ASR 暫設最多兩個並行；增加並行未證明可改善單段延遲。靜音檢查只能節省完全靜音片段的上傳，不能代替 WebRTC／模型 VAD。

## 本次測試與確切限制

- `dotnet build app-temp/api/Playback.Api.csproj`、`npm.cmd run build`（web、當時的 desktop 候選）已通過；本機 WASAPI capture 尚未做實機測試。
- 獨立測試 API 端口的 `/api/capture/status` 回報 `idle`、無效 session 的 Start 回 HTTP 409、閒置 Stop 安全返回；沒有在測試中開咪。
- `dotnet run --project app-temp/checks/Playback.Checks.csproj`：驗證 ASR 原文優先、靜音與未知 provider schema、web citation 映射與不安全 URL 拒絕。
- `node app-temp/archive/desktop/queue-check.mjs`：封存候選曾用合成 1,080 段、每段十秒的三小時時間線測試 queue。這**沒有證實**真實 48 kHz 連續三小時擷取、權限、CPU／磁碟吞吐或系統音源。
- `node app-temp/web/browser-check.mjs`：在本機 Edge 測等寬雙欄、主題、Markdown 的**有標籤實際 SVG 圖表**、Ask Playback、手機 tabs、Start Recording 按鈕，無頁面錯誤；測試沒有開咪。
- `node app-temp/dev-check.mjs`：不啟動 container，以替身函式測現有 DB、自訂連接字串、啟動及等待、Docker 失敗訊息。
- `node app-temp/web/asr-ui-check.mjs`：以攔截 API 測自動排隊、日期／時段樹、無逐段 Retry 及直接開始錄音，不上傳錄音。
- SenseVoice 靜音 fixture 在 2026-09-26 直接呼叫回 HTTP 200；此前版本的本機 API 單段 retry 曾保存一條空白但標示不確定的 transcript。新版本跳過完全數位靜音，並已用短篇真實語音完成端到端測試。Gemini／Jev 無憑證，因此只執行四個合成詞的簡單規則比較：4 個中 3 個符合標籤；兩種模型的 latency、confidence、usage／cost 均未測得。可明確 opt in 用 `POST /api/terms/evaluate-synthetic` 測合成詞。
- 本機收音的硬件權限、Windows loopback 無聲情況、較長真實語音及連續三小時穩定性仍需實測。流暢的即時筆記取決於 chunk 完成及 ASR latency；不是 streaming ASR。
- `node app-temp/api/integration-check.mjs` 已通過 session 隔離、note versions、chunk retry 與時間範圍測試；測試建立兩個合成 session 並保留它們，不會刪除資料。
- 目前只可附上文字教材。PDF 頁碼擷取、翻譯 job、講者辨識、長時間磁碟配額、分享權限及部署未實作。

## 相關官方資料

- [Microsoft Agent Framework 的 Google Gemini 整合](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)
- [Google Search grounding 與 annotation](https://ai.google.dev/gemini-api/docs/google-search)
- [Electron desktopCapturer](https://www.electronjs.org/docs/latest/api/desktop-capturer)
- [TypeSafe OpenAPI](https://api.typesafe.ai/docs)

## 本機端到端測試

先用 `pnpm.cmd dev` 啟動 API、Vite 和本機 MongoDB，然後在另一個 PowerShell 視窗執行：

```powershell
pnpm.cmd test:e2e
```

測試使用本機 Edge 和真實 UI/API/MongoDB：在 UI 建立獨立 demo session、附加文字教材、儲存筆記並重載驗證，再上傳一秒數位靜音 WAV，確認背景佇列自動標記為 `silent`、沒有產生假逐字稿。預設測試離線且不會把音訊送往 SenseVoice；每次會在 MongoDB 留下一筆有 `E2E demo` 標題的測試 session。改動 UI、API、儲存或 ASR 流程後，應重新執行此測試。

如已另行取得真實語音上傳授權，並有已完成轉寫的 session，可額外檢查真實逐字稿及日期／小時時間樹：

```powershell
$env:PLAYBACK_E2E_REAL_SESSION_ID = '<session-id>'
pnpm.cmd test:e2e
Remove-Item Env:PLAYBACK_E2E_REAL_SESSION_ID
```

2026-09-26 使用獲授權的 5.1 秒粵語 WAV 與 MongoDB 驗證：SenseVoice 直接請求約 2.28 秒，回報推理約 0.94 秒；新上傳到 API 約 17 毫秒獲接受，約 4.61 秒後 MongoDB 出現逐字稿。API 儲存的逐字稿有文字而沒有 SenseVoice 標記，UI 的時間樹亦通過 Playwright 檢查。這些是短片段的單次實測，未有人工標準稿比對文字準確度，也不能代表三小時錄音的吞吐量。
