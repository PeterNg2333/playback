# Playback UI 設計 demo（離線樣本）

這是一個用來討論版面與互動的靜態頁面。雙擊 `index.html` 即可在瀏覽器開啟，或執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ui-design-demo\study.ps1
```

加上 `-OpenLesson` 會在預設瀏覽器開啟頁面。預設指令只驗證三個必要檔案、HTML 主要區塊與離線限制，不會啟動錄音、容器或網絡請求。預期顯示 `PASS: offline UI design demo is ready.`。

## 先做的可行性判斷

`../07-poc-roadmap/README.zh-HK.md` 已把三小時錄音的 durable chunk、補傳、ASR 吞吐、snapshot 恢復列為 UI 之前的驗證 gate。現有 `scripts/00`–`07` 只提供分開的離線 study；通過其自測不等於完成端到端 POC。這個 demo 因此只用寫死的虛構課堂資料。未來進入 `app-temp/` 的 .NET 10 + React + Vite prototype 時，先驗證本機錄音與恢復，再接這個資訊架構。

`docs/scope-decision.zh-HK.md` 提出未來 MongoDB；現有 module 06 和 roadmap 採 PostgreSQL。這是資料持久化 POC 前需要定案的技術分歧，不影響此純前端 demo。

## 版面提案

- **兩欄平分**：左邊 Notes，右邊 Transcript；Sources 是右欄分頁，手機可切換三個畫面。
- **Markdown Notes + flowchart**：可在 Preview／Markdown 之間切換及即時預覽。筆記中的 `flowchart` fenced block 會變成圖表；重點筆記不附引用。
- **Transcript**：每段標出起訖時間，保留 ASR 原文及不確定標記；需要時才切換雙語。來源及 chat 回答仍有可回查的引用。
- **品牌方向**：頂部 Theme 提供 Pulse（建議主題：紫、粉紅、藍，聲音脈衝 icon）、Wave（藍、粉紅，聲波 icon）及 Loop（粉紫、藍，回放 icon）。切換時 logo 與色彩一起改變，供比較。

試用順序：切換主題 → 按 Markdown 編輯圖表節點 → 返回 Preview 查看圖表 → 切換 Transcript／Sources → 點 acoustic features 打開解釋。獨立的 Ask Playback 浮層仍可提問。鍵盤可用 Tab、Enter 和 Escape。

## 限制與下一步

頁面無真實錄音、ASR、Gemini、Jev、檢索、保存或共享。Markdown 支援標題、段落、清單、粗體、inline code、引用塊及有限的 `flowchart LR` 圖表語法（`A[Label] --> B[Label]`；最多 8 個節點、10 條邊，不接受循環）。不支援完整 Mermaid；無法解析時會保留原始圖表文字。編輯只留在目前頁面記憶體，重新整理便重置。預設問題及若干關鍵字的回答都是固定文案，不代表模型已生成；其他問題只會顯示來源不足。真正 UI 必須由 server snapshot 恢復、清楚顯示 recorder/上傳/ASR 狀態，並保留原始逐字稿、修訂及來源版本。
