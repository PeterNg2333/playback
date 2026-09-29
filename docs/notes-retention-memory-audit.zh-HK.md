# Week 3：舊筆記內容與 UI 記憶體核對

2026-09-29 實作補充：section保留／coverage、恢復preview、history分頁、詳解及真正逐字稿virtualization已加入；驗收與仍未完成項見 [實作報告](notes-redesign-validation.zh-HK.md)。本文件以下是2026-09-28修復前的只讀證據，不能當成目前程式路徑。恢復未套用、live API未更新；同一737快照的短測已能完成24次編輯，約14 mounted rows／962 DOM、GC約16.8MiB，沒有聲稱三小時無leak。

後續 frontend 專項已重現並修復 Mermaid 錯誤 DOM 累積、React 舊引用索引、Activity 閉合內容的成本及真正 scroll parent 錯誤。10 分鐘／2,700 初始 records＋292 finals／440 編輯循環完成，定期 GC 約15–16.4MiB，切小 session 舊 source IDs 為0。詳見 [frontend memory 配對證據](frontend-memory-validation.zh-HK.md)。這亦更正先前只用 mounted rows／programmatic reveal 驗證 virtualization 的不足；三小時 wall-clock、使用者 live browser 仍未驗證。

2026-09-28；本輪只讀取本機 API、保存快照、使用隔離瀏覽器重播資料及更新交接文件。沒有更改 live notes／DB、重啟錄音服務、呼叫新的外部 provider、上傳音訊或提交 Git。以下版本及數字屬有時間的快照，不代表之後最新版本。

## 舊筆記確實被下一次生成省略

Session `caf38f3f6ace470fa1f6b7dde15f46ed`（Week 3）在 v112 → v113 發生明確的內容丟失。

| 核對 | v112 | v113 | 後續 v118 |
|---|---|---|---|
| 保存時間（香港） | 20:47:29 | 20:50:51 | 20:58:48 |
| 保存 Markdown 字元（含來源 ID） | 67,351 | 19,949 | 24,041 |
| 公平分配目標／十條 flow 分享 R 的例子 | 有 | 缺失 | 未補回 |
| speed-test 局部測量限制、附近 server／RTT | 有 | 缺失 | 未補回 |
| 安全、Wireshark、加密／quantum、botnet | 有 | 缺失 | 未補回 |
| 五層架構／封裝與各層處理 | 有 | 缺失 | 未補回 |
| 來源 metadata 累積數量 | 635 | 667 | 730 |

缺失項逐一對照版本正文及來源，不只比較標題。v118 雖包含 edge／client-server／IPv4 等新內容，亦提到 server bottleneck，但沒有保留前段公平分享例子及上述早段說明。v113 的 Activity 是實際 Vertex／`gemini-3.1-flash-lite` note revision，從 v112 開始、20:50:44 發起、顯示 completed。API 保存的 Markdown 已缺內容，因此問題位於生成／保存流程，並非單純 UI 隱藏。

更早 v103–v110 的 Markdown SHA-256 完全相同（`c582d5561a6ce05b6628c0e4915015f42651228f1da0a081eda134a1eba7810d`），但登記來源由 533 增至 611。這證明 source metadata／completed 不能直接當作筆記真正消化了所有來源的證據；尚未逐條評分這 78 個追加來源的內容。

### 為何一般更新不會自然補回

- [NoteAgent.cs](../app-temp/api/Services/Ai/Agents/NoteAgent.cs) 將既有整份 note 放進 BASE VERSION，再要求模型回傳「complete revised note」。舊 note 確實有送入現行程式的 input；目前没有保存每次完整 provider request，不能逆向證明當次供應商實際讀取了甚麼。
- 模型結果整份交給 `SaveGeneratedNote` 保存。現有 validation 檢查引用、語言及 note base version，沒有檢查舊主題／推導／例子是否消失。v113 雖刪掉內容仍通過「Validated and saved」流程。
- `TranscriptIds` 會和 previous 全部來源取 union，舊 transcripts 的 `noteStatus` 亦維持 completed；正文缺失並不會將它們重新排入 pending。
- manual revision 在沒有 pending 時只重送最後 40 段，不能完整重建早段。現有「每題 2–4 bullets／約 30 words」與全篇 4096-token 限制可能增加過度壓縮的壓力；這是 code-supported 的風險，**並非已證實的模型內部原因**。
- `NoteHistory` 和 restore 查詢只取最近 100 版；本快照返回 v19–v118。更早版本是否仍在 DB 未作直接查詢。這個 API 上限會令再早的保存版本無法經目前 restore 入口找到。

### 已保留的查核及恢復素材

完整 API history／session／Activity 快照在本機 ignored [資料目錄](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/notes.json)。另有原始來源標記完整保留的 [v112](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/v112-original.md)、[v113](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/v113-original.md)、[v118](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/v118-original.md) 和 [版本 inventory](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/inventory.json)。`*-readable.md` 只供排除已知引用後比較正文，恢復以 original／JSON 為準。

本輪未直接 restore 舊版：單獨 restore v112 會把後段新內容換掉，而且下一次全篇 revision 仍可能再刪早段。交接要求先修內容保留，再以可審閱 preview 合併新舊內容成新版本。

## 圖表、解釋及高亮詞的最新偏好

以更多有來源的圖表和更完整解釋提升理解，短 takeaways 之外保留必要的定義、符號、條件、推導、例子及論點。公平分享 `R/n`、十條 flow 的 `R/10`、五層／封裝和安全關係可作本 session 的驗收題材；需要對照原話及 ASR 不確定性，不因畫圖而補造事實。

現在 grounded term prompt 明確限制一個定義＋最多兩点／一例、90 words，輸出上限 768 tokens；TermExplanation 只呈現一份保存 explanation，沒有短版／詳解結構。因此「高亮可解釋更多」需要調整實際 request／保存結構和展開閱讀，不只放大 popup。hover 短版、展開詳解及適合的圖表可共享已保存结果，並區分課堂內容與外部補充。v118 的 `details` 說明亦顯示要檢查普通詞高亮是否有幫助。

## 記憶體與更新成本

使用者截圖顯示 JS heap 約 202 MB、短時間增加約 8.6 MB/s；這不是保存的 heap snapshot，不能單憑畫面判定哪些物件持續被保留。

靜態路徑已確認：

- [TranscriptContent.tsx](../app-temp/web/src/pages/TranscriptContent.tsx:205) 每次重新建立全堂 timeline，逐日／小時 `entries.map(renderEntry)`；即使 details 收起，其 child DOM 仍存在。沒有 list virtualization。
- [handlers.ts](../app-temp/web/src/pages/handlers.ts:119) 每 4 秒取完整 session，finalized audio 亦會重取；每次解析新物件後 `setSession`。本快照的 session JSON 約 1,738,393 bytes，含 737 transcripts／745 chunks。session snapshot 的輪詢沒有共享 in-flight 保護，慢回應可與其他刷新重疊。
- capture 每 250 ms 查狀態；[useActivity.ts](../app-temp/web/src/pages/useActivity.ts:21) 每 700 ms 更新 NotesPanel。整個 PlaybackController 的狀態與 model props 使 capture／session／editor 的變化重跑 transcript 路徑；NotesPanel 的 Activity 更新亦重新執行 Markdown renderer。
- 每列 `TranscriptRow` 重新 filter termInsights，Markdown 每次新建 source Map、plugin options 及 references context。這些是重算／配置路徑，不代表每個 Map 或 listener 都是 leak。

本輪使用已安裝 `playwright-core`，另開 headless Edge、Vite dev build、攔截全部 API 以保存資料回應，封鎖外部網站及所有非 GET。沒有操作使用者 Chrome、錄音或 live note；這個結果不能代替使用者原有 Chrome tab 的 retained-object 分析。

在 737 個 confirmed transcript 的重播中，baseline 738 mounted rows（含一列其他狀態）、17,210 DOM elements、39,281 CDP nodes。持續 interim 更新時，heap used 升至 182.35 MB／total 245.95 MB，強制 GC 後降至 47.00 MB／49.70 MB；四及八次 Edit／Preview cycles 後，GC heap 分別為 48.19 MB／41.81 MB。這些測量顯示大量 allocation churn；在已測到的階段尚未證實無界持續保留。實際操作也受明顯 CPU 成本影響。

十二次 Edit／Preview 後 GC heap 為 54.01 MB，之後再 GC 為 48.92 MB。初次壓測的 idle fixture 計時仍在變動，故不把其 idle 階段當作無變化 baseline 的證據；以上 interim 階段是明確模擬持續更新。

合成擴展至 2,700 transcripts 時，mounted rows 為 2,702、DOM elements 54,564、CDP nodes 125,792；GC heap 91.63 MB。隨持續 interim 更新增至 used 258.53 MB／total 321.20 MB。原 audio chunks 沒有相應增加，這只是列表壓力 fixture，並非真實三小時雙 source 音訊驗收。名為 3／6／9 秒的階段代表腳本要求的等待，**實際 checkpoint 分別耗時約 53／38／37 秒**，不能把標籤當成實際時間。由於 main-thread 負載令取樣／互動嚴重延遲，停止了已確認身份的本輪 audit Node 程序；未完成 scale 下的 24 次編輯、最終 GC 或切回小列表。停止後核對 live API build／startedAt 未變，仍在錄音。

重新執行修正過的穩定 idle／一次 local Edit→Preview：baseline GC used 46.35 MB，無新內容輪詢期間升至 147.53 MB，GC 後 51.14 MB；Edit→Preview 之後 GC used 48.62 MB、DOM nodes 39,283，接近原來 39,281；暫時增加的 Documents 從 4 回到 2。測試完整結束、沒有 page errors 或非 GET 嘗試。短測支持 allocation churn 的判斷，沒有證明所有情境均無 leak；user Chrome retained paths、live draft 長時間更新、來源 popup／播放及 scale 回落仍未測。

Artifacts：

- [持續更新與長列表的實際 checkpoints](../output/playwright/notes-memory-2026-09-28T13-03-04-813Z/observed-checkpoints.json)，由工具 console 原始輸出整理，明示中止及未完成階段。
- [穩定 idle／編輯測試完整結果](../output/playwright/notes-memory-2026-09-28T13-11-54-826Z/results.json) 及已目視的 [截圖](../output/playwright/notes-memory-2026-09-28T13-11-54-826Z/stable-idle.png)。
- [本地測量程式](../output/playwright/notes-memory-audit.mjs)；可用 `node output/playwright/notes-memory-audit.mjs --idle-only` 重播保存的資料。完整 scale 模式沒有資源限制，後續使用前應加測試時間／資源上限，避免再次飽和。

下一個 session 的修復及驗收，包括真正虛擬化、正確引用跳轉、增量資料／單一 in-flight、穩定物件及索引、避免無關狀態更新重算全堂、定期合併但保留原始 ASR，以及 GC 前後／DOM／CPU 比較，已列入 [implementation prompt](notes-redesign-handoff.zh-HK.md)。本輪沒有修改應用程式或聲稱記憶體問題已修好。
