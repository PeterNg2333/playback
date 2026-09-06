# ASR + LLM Note spike study 計劃

## 範圍契約

這次只建立 `scripts/` 內可獨立閱讀及執行的學習模組，不建立 production app、solution、API server、microservices 或正式 UI。所有會接觸網絡、安裝軟件或啟動 container 的操作，都必須由使用者明確選擇參數。

## 模組與驗收條件

| 模組 | 要回答的問題 | 最低驗收 |
|---|---|---|
| 00 setup | 本機能否做 .NET 10 開發？ | 顯示 SDK / VS Code / Docker 狀態；安裝需 opt-in |
| 01 C# | 初學者如何讀及跑 C#？ | 單檔 app 可執行並示範核心語法 |
| 02 audio/VAD | 為何錄音不應由網頁生命週期擁有？ | WAV/VAD 小實驗可重現，檔案只留本機 |
| 03 ASR adapter | 公司 SenseVoice endpoint 是否可包成 adapter？ | 預設 dry-run；明確 opt-in 才送 WAV |
| 04 note agent | 如何週期性把 transcript 修訂成筆記？ | offline deterministic demo；Gemini 明確 opt-in |
| 05 chat/search | 如何讓提問有網上證據？ | offline evidence demo；Wikipedia 搜尋明確 opt-in |
| 06 PostgreSQL | 初學者如何用 Compose 學 local PG/SQL？ | 預設只檢查；Start/Stop 明確 action |
| 07 roadmap | POC 後應按甚麼次序驗證？ | 列出決策閘門、風險及手機方向 |

## 驗證節點

1. 結構：每個 module 都有 `README.zh-HK.md`、`study.ps1`、`index.html`。
2. 離線：所有 default command 不依賴 API key，也不發送 lecture data。
3. 邊界：secret 只由 environment variable 讀取；URL 固定 allowlist；SQL 範例參數化。
4. 可學習：README 說明「為甚麼」，HTML 提供閱讀路線，script 顯示可觀察結果。
5. POC 結束：只產生研究結果，不把 spike 誤稱為可部署 MVP。

## 已知風險

- Lecture audio 可能包含個人資料或受版權保護內容；正式測試前須取得同意並訂 retention policy。
- Browser/PWA 的頁面、權限和背景生命週期不能作長錄音的唯一保障。
- ASR response schema 尚未有正式公司 contract；adapter 必須保存 raw response 並容忍 schema 演進。
- LLM 每數分鐘重寫全文會愈來愈貴；正式版本應用 transcript watermark、revision history 和 bounded context。

