# .NET ASR + LLM Note：spike study

## 完全未學過 C#，先從這裏開始

先用瀏覽器打開 [C#／.NET 零基礎 HTML 教學](./csharp-dotnet-tutorial.html)，可以直接雙擊檔案，毋須先執行 `study.ps1`。

教學從 C# 與 .NET 的分別、SDK、建立 Console 專案開始，再逐行解釋 `Console.WriteLine`、變數、字串、輸入、`if / else`、集合、迴圈與方法。每課提供完整程式、預期輸出和可展開的練習答案，最後親手完成課堂筆記程式。自己的練習放在 `scripts/my-csharp-practice/`，建立方式在教學內。

完成基礎後，如果覺得 [module 01](./01-csharp-basics/README.zh-HK.md) 一次過講 record、LINQ 和 async 太多，可以先讀 [C# 中階銜接教學](./csharp-dotnet-tutorial-2.html)：逐個概念拆做獨立練習檔案（放喺 `demo/csharp-practice/`），自己動手寫、逐個跑，最後組成迷你版本，再回頭對照 module 01。

這個資料夾是學習用 POC，不是 final MVP。它把問題拆成八個可獨立執行的實驗，讓你先確認技術是否可行，再決定是否建立 `audio client / AI server / UI` 三個正式服務。

## 已確認的兩件事

- 本機已安裝 .NET SDK 10；重新開 VS Code terminal 後可用 `dotnet --version` 檢查。
- 公司 SenseVoice 路徑已以 WAV probe 驗證：`POST https://dev-aks.setsailapi.com/stt/infer/upload`，multipart 欄位是 `file`。真正發送仍是 module 03 的 opt-in 操作。

## 建議學習順序

| 次序 | Folder | 你會學到 |
|---:|---|---|
| 1 | `00-setup-dotnet` | SDK、runtime、CLI、VS Code 與 Docker 的角色 |
| 2 | `01-csharp-basics` | C# types、record、collection、LINQ、async/await |
| 3 | `02-audio-vad` | local audio ownership、WAV、chunk、energy VAD |
| 4 | `03-sensevoice-adapter` | multipart upload、timeout、raw response、adapter boundary |
| 5 | `04-live-note-agent` | transcript watermark、rolling revision、Gemini + Agent Framework |
| 6 | `05-live-chat-search` | evidence-first search、citation、prompt grounding |
| 7 | `06-postgres-compose` | Compose、PostgreSQL schema、PK/FK/index/query |
| 8 | `07-poc-roadmap` | POC 決策閘門與日後實作次序 |

每個 folder 都有三個入口：

- `README.zh-HK.md`：概念、限制、逐步解釋。
- `study.ps1`：安全且可重現的小實驗；不帶參數時不會傳 lecture data 到外部。
- `index.html`：可直接用瀏覽器開啟的教學頁，不需要 web server。

## 完成基礎教學後，第一次執行實驗

在 VS Code 選擇 **File → Open Folder**，開啟這個 repository，然後在 PowerShell terminal 執行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
& .\scripts\00-setup-dotnet\study.ps1
& .\scripts\01-csharp-basics\study.ps1
& .\scripts\04-live-note-agent\study.ps1
& .\scripts\verify-all.ps1
```

若目前 terminal 是安裝 SDK 前已開啟，PATH 可能未刷新；關掉 terminal，再開一個即可。Scripts 也會嘗試 Windows 預設安裝位置 `C:\Program Files\dotnet\dotnet.exe`。

## 需要明確 opt-in 的操作

```powershell
# 實際把 WAV 傳到公司 endpoint
& .\scripts\03-sensevoice-adapter\study.ps1 -WavPath .\sample.wav -Send

# 實際呼叫 Gemini；key 只從目前 process environment 讀取
$env:GOOGLE_AI_STUDIO_API_KEY = '你的 key'
& .\scripts\04-live-note-agent\study.ps1 -Mode Gemini

# 實際使用 Wikipedia 的固定搜尋 endpoint
& .\scripts\05-live-chat-search\study.ps1 -Mode Wikipedia -Query 'voice activity detection'

# 明確啟動 local PostgreSQL container
& .\scripts\06-postgres-compose\study.ps1 -Action Start
```

不要把 API key 寫進 `.cs`、`.ps1`、HTML 或 git。Lecture audio/transcript 在送往公司 ASR 或 Gemini 前，也應先確認 lecturer、同學及校方的錄音與第三方處理規則。

## 這次刻意沒有做

- 沒有 browser microphone UI、desktop recorder 或 mobile app。
- 沒有長時間 background service、WebSocket/SSE、正式 REST API。
- 沒有 production database migration、authentication、deployment。
- 沒有聲稱 energy threshold 就是 production VAD。
- 沒有把 Gemini、搜尋或 endpoint error 靜默改成 mock 成功。

## 一次驗證結構

```powershell
& .\scripts\verify-all.ps1

# 另跑各 module 的安全 default study
& .\scripts\verify-all.ps1 -RunStudies
```

技術選擇及驗收契約見 [`STUDY-PLAN.md`](./STUDY-PLAN.md)，可行性結論見 module 07。

## 官方參考

- [.NET 10 file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps)
- [Microsoft Agent Framework：Google Gemini](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)
- [Gemini models](https://ai.google.dev/gemini-api/docs/models)
- [Docker Compose](https://docs.docker.com/compose/)
- [PostgreSQL tutorial](https://www.postgresql.org/docs/current/tutorial.html)
