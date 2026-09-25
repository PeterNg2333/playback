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

## 未來產品的研究與實作原則

- 產品方向見 `docs/scope-decision.zh-HK.md`。現階段仍只做 `scripts/` spike；以下是未來驗證準則，不代表已決定建立正式 client 或 API。
- Desktop 目標是全機播放聲音，不是指定視窗。Electron + TypeScript 是候選；Windows 與 macOS 的收音、權限及錄到無聲 stream 的情況須分別驗證。若也錄自己的聲音，咪高峰另作來源；保留來源及時間資訊，再決定是否混音。手機只考慮外置／內置咪高峰，長時間切 app／鎖屏的可靠性要用 native client 驗證。
- ASR 原文、LLM 翻譯及修訂版本分開保存，連結到音訊時間戳。聽不清的片段標示不確定；模型推測不能無痕覆蓋原文。
- 先由教材及已確認逐字稿產生術語候選。Jev 只作候選詞排序、提示解釋與否等結構化判斷候選；解釋文字由 Gemini 根據來源生成。不要從錄音推斷某位學生是否真的不懂。採用 Jev 前比較簡單規則及 Gemini 分類的效果與成本。[TypeSafe AI 對 Jev 的介紹](https://typesafe.ai/blog/introducing-system-one-models-and-jev)
- NotebookLM 的公開產品行為可借鑑來源定位與引用，但其內部架構未公開，不聲稱複製其實作。[Google NotebookLM 介紹](https://blog.google/innovation-and-ai/technology/ai/notebooklm-google-ai/)；開源 [Open Notebook 的概念](https://github.com/lfnovo/open-notebook/blob/main/docs/2-CORE-CONCEPTS/index.md)及[架構](https://github.com/lfnovo/open-notebook/blob/main/docs/7-DEVELOPMENT/architecture.md)可作來源／筆記／對話分層及檢索流程參考，不沿用其技術棧作預設。
- 教材、投影片、逐字稿及筆記按課堂隔離；保留教材頁碼、音訊時間戳及版本。長資料按問題檢索相關片段，再由 LLM 回答及附可回查引用；即時問題只使用已處理的資料，區分來源內容與模型推論。
- 多個 client 補錄同一課堂時，以 session、source、時間及重疊資訊對齊及去重，不能只順序拼接。共享錄音／ASR／筆記須按權限控制；個人 AI 問答預設私人，不因加入共享課堂而公開。分享前確認講者、同學及校方同意、私隱和保留期限。

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

## 程式碼可讀性與維護性

- 遵守 Locality of Behavior 與 KISS：相關邏輯放在一起，避免把短而連貫的流程拆成大量微型檔案、helper 或函式。只有確有多個實作或明確替換需要時才引入 interface、abstract class 或 factory；不要加入企業式樣板架構。
- 用精確而簡潔的命名表達意圖。註解只解釋決策原因、業務限制或非直觀 workaround；不要用註解逐行複述程式碼。
- 程式流程由上而下，使用 guard clause 及 early return，條件巢狀原則上不超過兩層。一個函式完成一項完整的邏輯工作；不要純為縮短行數拆散線性流程。
- 型別保持直接、易懂，避免妨礙閱讀的複雜泛型。錯誤在合適的位置明確處理；不要吞例外或用籠統的 catch-all 掩蓋失敗。
- 測試可觀察的行為、邊界條件、業務規則及整合點；不要為簡單 private helper 或覆述實作的細節寫測試。避免為測一個結果而 mock 大量內部依賴，也不要追求無意義的 coverage 數字。
- 每個實作及測試階段結束前做一次「human code smell review」：檢查深層巢狀、意外複雜度、錯誤抽象、重複業務規則及命名。若開發者無法在約 30 秒內理解一段程式碼的主要流程，先簡化再交付。

## 常用驗證

```powershell
dotnet --version
& .\scripts\verify-all.ps1
& .\scripts\verify-all.ps1 -RunStudies
& .\scripts\06-postgres-compose\study.ps1 -Action Check
```

完整學習順序見 `scripts/README.zh-HK.md`；POC 決策閘門見 `scripts/07-poc-roadmap/README.zh-HK.md`。
