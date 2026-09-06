# Playback agent guide

## 目標

研究一個以 .NET 為基礎的即時 ASR + LLM lecture-note 工具，主要給學生及會議使用，並能支援約 3 小時的長錄音。

期望流程：

1. 本機 audio client 錄音、分段並先保存 audio，避免 transcript 或網絡失敗造成資料遺失。
2. Local VAD 判斷語音片段，再經 ASR adapter 呼叫公司 SenseVoice。
3. 每 N 分鐘由 LLM 修訂 rolling notes，並可參考 lecture materials。
4. UI 即時顯示 transcript／notes，並提供帶網上搜尋證據的提問功能。

## 已選方向

- Runtime：.NET 10、C#。
- LLM orchestration：Microsoft Agent Framework；不要使用 Semantic Kernel。
- LLM：以 Gemini Flash／Flash Lite 為主；API key 名稱是 `GOOGLE_AI_STUDIO_API_KEY`。
- ASR：adapter pattern；首個 endpoint 是 `POST https://dev-aks.setsailapi.com/stt/infer/upload`，multipart field 是 `file`。
- Database study：本機 PostgreSQL + Docker Compose。
- 未來可分成 `audio client / AI server / UI`，但 spike 階段不要過早建立 microservices。

## Audio 與 UI 原則

- Browser 不應是長時間錄音的唯一 owner；refresh、switch page 或 background throttling 不可導致錄音遺失。
- Desktop/native local process 負責 capture、durable chunks、retry；browser UI 只負責控制、查看及提問。
- 手機第一階段只做 UI。若日後用手機靠近 lecturer 收音，應使用 native capture + local queue，而不是只靠網頁錄音。

## 目前工作範圍

- 現階段只做 `scripts/` 內的可行性 spike 和初學者教材，不是 final MVP。
- 不建立正式 API server、background service、desktop/mobile app、authentication、deployment 或 production migration，除非使用者另行要求。
- 每個 study module 使用一個 folder，至少包含：
  - `study.ps1`
  - `README.zh-HK.md`
  - `index.html`
- 文件要適合首次學習 .NET、C# 和 SQL 的使用者，解釋原因、執行方法、預期輸出及限制。
- 不要把 demo、mock、energy-threshold VAD 或 spike schema 稱為 production-ready。

## 安全與資料限制

- Default study command 必須離線、可重現，且不傳送 lecture data。
- 網絡請求、安裝軟件、啟動 container 或上傳 audio 必須是明確 opt-in。
- Secret 只從 process environment 讀取；不要把 key 寫入或輸出到 `.cs`、`.ps1`、HTML、log 或 Git。
- 不讀取、修改或提交 `.env`，除非使用者明確要求。
- 上傳 lecture audio、transcript 或 materials 前，提醒確認 lecturer、同學及校方的同意、私隱和 retention 規則。
- 外部 URL 使用固定 endpoint／allowlist、timeout、response-size limit，並避免 redirect 洩漏資料。
- 保存 audio chunk 時使用 session/source/sequence/hash 等穩定 identity，讓 retry 可冪等並能 batch recovery。

## Agent 工作規則

- 修改前先讀相關 README、script、test 及既有 pattern；保留使用者未提交的變更。
- 未經要求，不擴大到 `scripts/` 以外；`AGENTS.md` 本身是此規則的例外。
- 優先做最小、可驗證的 spike，不自行補成完整架構或 final MVP。
- 使用官方文件確認會變動的 .NET、Agent Framework、Gemini、Docker 和 PostgreSQL 用法。
- 更改行為時加入或更新小型 deterministic test；不要以 mock success 掩蓋外部服務錯誤。
- 測試後清理 `.artifacts/`、`.nuget/`、`bin/`、`obj/` 等生成物。
- 不 commit、push、刪除使用者檔案或執行 destructive database action，除非使用者明確要求。

## 常用驗證

```powershell
dotnet --version
& .\scripts\verify-all.ps1
& .\scripts\verify-all.ps1 -RunStudies
& .\scripts\06-postgres-compose\study.ps1 -Action Check
```

完整學習順序見 `scripts/README.zh-HK.md`；POC 決策閘門見 `scripts/07-poc-roadmap/README.zh-HK.md`。
