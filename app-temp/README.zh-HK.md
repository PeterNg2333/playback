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
                                                    └── SenseVoice (separate opt in)
```

錄音先在 `app-temp/data/local-capture` 完成每段 WAV，再匯入 `app-temp/data/audio`；匯入失敗的完整 WAV 會留待下一次 Start 時重試。MongoDB 只存 metadata。原始 ASR 為獨立欄位，翻譯及修訂不覆蓋它。材料與 transcript 查詢依 session ID 隔離。私人問題只留在當前 UI 狀態，暫未持久化。

## 執行

請先自行提供**本機 MongoDB** 於 `127.0.0.1:27017`。`compose.yaml` 提供一個只綁定 localhost 的 [官方 Mongo image](https://hub.docker.com/_/mongo/)；開啟 Docker Desktop 後，可在 repo root 執行以下指令。`db:up` 會啟動 container，首次可能下載 image；`setup` 會還原 web 及 .NET 依賴，首次可能下載套件。若已有本機 MongoDB，略過 `db:up`，並可用 process environment `PLAYBACK_MONGO_URI` 指定連接字串；不要放進 `.env` 或提交。

```powershell
pnpm.cmd setup
pnpm.cmd db:up
pnpm.cmd dev
```

SenseVoice 不再需要額外啟動開關。開始新錄音前，若已確認外部處理同意並勾選網頁底部方格，API 會先保存每段音訊，再在背景逐段自動轉寫；不需逐段按 Retry。勾選前已保存的錄音，可在 **Sources → Transcribe all pending audio** 排隊處理。沒有同意時仍可只在本機錄音。失敗的片段會顯示 `asr-error` 和原因，保留原音訊供重試。

`dev` 在同一個終端啟動 API 與 Vite；不會自行啟動 MongoDB、錄音或外部 ASR。瀏覽器開 `http://127.0.0.1:5173/`。若兩個新版服務已在運行，再執行 `dev` 只會提示開網頁；若只運行其中一個或 API 是舊版，先在原來終端按 `Ctrl+C`，再執行 `pnpm.cmd dev`。錄音時先按網頁的 Stop Recording，再於終端按 `Ctrl+C` 停止兩個程式。要停止 MongoDB，可執行 `pnpm.cmd db:stop`，資料 volume 會保留。Windows PowerShell 使用 `pnpm.cmd`，因為本機執行原則可能封鎖 `pnpm.ps1`。

先建立 session，再在 Transcript 頁按 Start Recording。API 會先開預設咪高峰，再嘗試錄預設播放裝置的聲音；兩者分開保存。每十秒完成一段 WAV，Stop 後在 Sources 查看和播放。收音發生在**運行 API 的那部 Windows 電腦**，遠端 API 無法錄到你電腦的聲音。此功能尚未完成實機權限、靜音 stream 或連續三小時驗證；若 API 被強制終止，最後尚未封口的 `.wav.part` 需要人工檢查。

預設不把資料送去外部服務。**傳送真實 lecture audio、transcript 或材料前，先確認 lecturer、同學、校方的同意，以及私隱與保留期限。** UI 的確認方格及 API header 是防止意外上傳的操作閘門，並非權限系統。Gemini 與 Jev 各自只從 process environment 讀 `GOOGLE_AI_STUDIO_API_KEY`、`JEV_API_KEY`。沒有 key 時顯示 unavailable；不會回傳假成功。

## 本次測試與確切限制

- `dotnet build app-temp/api/Playback.Api.csproj`、`npm.cmd run build`（web、當時的 desktop 候選）已通過；本機 WASAPI capture 尚未做實機測試。
- 獨立測試 API 端口的 `/api/capture/status` 回報 `idle`、無效 session 的 Start 回 HTTP 409、閒置 Stop 安全返回；沒有在測試中開咪。
- `dotnet run --project app-temp/checks/Playback.Checks.csproj`：驗證 ASR 原文優先、靜音與未知 provider schema、web citation 映射與不安全 URL 拒絕。
- `node app-temp/archive/desktop/queue-check.mjs`：封存候選曾用合成 1,080 段、每段十秒的三小時時間線測試 queue。這**沒有證實**真實 48 kHz 連續三小時擷取、權限、CPU／磁碟吞吐或系統音源。
- `node app-temp/web/browser-check.mjs`：在本機 Edge 測等寬雙欄、主題、Markdown 的**有標籤實際 SVG 圖表**、Ask Playback、手機 tabs、Start Recording 按鈕，無頁面錯誤；測試沒有開咪。
- `node app-temp/web/asr-ui-check.mjs`：以攔截 API 測舊版提示、同意確認、舊錄音排隊及新錄音自動轉寫授權，不上傳錄音。
- SenseVoice 靜音 fixture 在 2026-09-26 直接呼叫回 HTTP 200；本機 API 的單段 retry 亦成功保存一條空白但標示不確定的 transcript。這只驗證連通、格式與寫入，未驗證真實語音辨識。Gemini／Jev 無憑證，因此只執行四個合成詞的簡單規則比較：4 個中 3 個符合標籤；兩種模型的 latency、confidence、usage／cost 均未測得。可明確 opt in 用 `POST /api/terms/evaluate-synthetic` 測合成詞。
- 本機收音的硬件權限、Windows loopback 無聲情況、真實語音辨識及連續三小時穩定性仍需實測。流暢的即時筆記取決於 chunk 完成及 ASR latency；不是 streaming ASR。
- `node app-temp/api/integration-check.mjs` 已通過 session 隔離、note versions、chunk retry 與時間範圍測試；測試建立兩個合成 session 並保留它們，不會刪除資料。
- 目前只可附上文字教材。PDF 頁碼擷取、翻譯 job、講者辨識、長時間磁碟配額、分享權限及部署未實作。

## 相關官方資料

- [Microsoft Agent Framework 的 Google Gemini 整合](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)
- [Google Search grounding 與 annotation](https://ai.google.dev/gemini-api/docs/google-search)
- [Electron desktopCapturer](https://www.electronjs.org/docs/latest/api/desktop-capturer)
- [TypeSafe OpenAPI](https://api.typesafe.ai/docs)
