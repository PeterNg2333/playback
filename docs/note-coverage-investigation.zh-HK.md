# Week 3 筆記保留及來源缺口調查

2026-09-29，核對原 session `caf38f3f6ace470fa1f6b7dde15f46ed` 的實際 API／Mongo 保存內容，並重播隔離快照。原錄音、transcript、v146 及其歷史未改寫，沒有清空資料庫或 commit。

## 發現

這次不是單純沒有重啟。5078 實際 health 已是 `section-notes-v6`，但舊流程遺留的完成狀態沒有跟正文重新核對。

- v146 有 1,156 段非空 transcript；正文實際引用 46 段。其餘 **1,110 段全標為 completed，卻沒有筆記引用**。
- 缺口由 session 0:00 至 139:04，1,110 段合計有約 138 分鐘語音。此統計排除靜音間隔，按 source 分開，不把 mic／system 重疊時間相加。
- v145 已只剩 TLS 相關主題，正文引用同樣是 46 段。v145→v146 的 Markdown 27,859→5,620 字元主要反映引用壓縮；移除來源標記後正文反而由 2,881→4,519 字元。不能把這次字元縮減直接當作最新 section 更新再次刪掉全堂內容。
- v141–v145 仍走舊全篇筆記模式，登記來源達 1,103–1,156 段，正文卻只有 31–46 段引用。較早主題的缺失已存在於舊版本；來源 metadata 及 completed 不能證明正文保留了它們。
- 原先 Pending 路徑排除 completed；普通 Revise 沒有 pending 時只重整現有 section，因此不會自然重建已丟掉的早段。

只讀快照及逐版統計：[本輪資料](../app-temp/data/validation/runs/2026-09-29-note-coverage/)，包括 `session-before.json`、`history-before.json` 和 `post-guard/offline-results.json`。

## 本輪修正

1. `NoteCoverage` 從**保存正文實際出現**的來源標記及 persistent citation 計算引用，忽略 completed／登記來源的虛假完整性。連續未引用語音達 **120 秒**列為大缺口；此閾值是提示政策，不是兩分鐘內可任意漏內容的允許。
2. Notes 顯示未引用數量、最早缺口時間、曾標為 completed 的缺口，並可跳回原 transcript。deferred 仍是未完成；有意刪除／suppressed 不自動補回。
3. **Repair earliest gap with AI** 每次只處理最早缺口的一批，最多 32 段／9,000 字元，包含舊 completed 及 deferred。它附加 section、保留既有 section，不全篇重寫。單個超限來源仍受現有 input 上限保護；不截斷文字以假裝成功。
4. 普通更新及 organizer 都保留被改寫時漏掉的既有 point 正文；`retains` 或相同 source IDs 不足以免除這個保護。引用次序正規化避免只有 `[a,b]`／`[b,a]` 差異時重複補上相同 point。
5. Mongo 寫入前新增保留檢查。AI 結果若刪整個舊 section、漏掉舊 point 正文，或更動未獲選擇整理的 protected section，拒絕保存。明確 organizer 可改所選 section 排版，仍須保留 point 正文。
6. 補寫中未被寫成 point 的來源保持 pending／deferred，不維持假的 completed。未保存 editor 內容及舊 base version 均阻止套用。

Prompt 更新為 `section-notes-v7`／`section-organize-v7`。仍使用原本 section contract，沒有引入第二份 note 或新的外部審稿 agent。

有引用不等於有完整解釋：把所有來源掛在一句空泛總結仍可能通過引用檢查。這層保護能偵測大段來源完全沒有引用及阻止已寫 point 被刪；新生成内容的定義、公式、條件、例子、論點是否準確，仍要對原文審閱。Google 的 [structured output 指引](https://ai.google.dev/gemini-api/docs/structured-output) 亦要求應用驗證值，schema 本身不能驗證語意。

## 重跑結果及邊界

| 證據 | 結果 |
|---|---|
| 完整離線快照重播 | 1,156 段來源，35 批補寫；未引用 1,110→0。每次既有 section 完整保留。fixture 複製輸入原文，**不是自然 AI 筆記品質通過**。 |
| 故意遺漏內容的模型 fixture | Organizer 宣稱保留 ID／引用卻刪 R/n、R/10／共享 bottleneck 條件，原 point 仍保留；直接刪 section 在 persistence guard 被拒絕。 |
| .NET／前端 | Build、protocol、notes-redesign 檢查通過。前端仍有既有大型 bundle／Zod annotation 警告。 |
| 真實 MongoDB | 新 synthetic session 留在 `playback_e2e`；AI 刪除被拒後 head 未變，保留正文的更新成功讀回，v1 歷史仍存在。 |
| Offline browser | 原 Week 3 快照顯示 1,110 缺口；source reveal、dirty editor protection、修復 base、stale error、390px Notes 無橫向 overflow 通過，畫面已檢查。 |
| 真實 API 只讀 | 5081 已載入 v7／`noteCoverage:true`，本機 Mongo=true；既有一小時 validation session 實際 137 段中 42 引用、95 未引用、7 個兩分鐘大缺口，沒有把 deferred 當已覆蓋。 |
| 真實 Gemini 重跑 | 自動審批拒絕：simulate 未明確授權傳 Week 3 文字到 Google 及付費呼叫。已提出最多三次文字補寫選項；未取得回覆前不發請求。 |

新 API build `2ec1c818-ad3f-4ab4-965a-530435f7941a`。Review 網頁 [5177](http://127.0.0.1:5177) 使用 5081／`playback_e2e`；自動 notes／terms／ASR 保持關閉。原 5078／`playback_prototype` 仍為 v6，未重啟；原 Week 3 並未套用 offline fixture 或歷史 restore。日常服務須先停止錄音並重啟 backend，才會提供新 coverage API；新版 UI 對此明示。

測試與畫面：[`store-results.json`](../app-temp/data/validation/runs/2026-09-29-note-coverage/store-results.json)、[`review-health.json`](../app-temp/data/validation/runs/2026-09-29-note-coverage/review-health.json)、[`review-coverage.json`](../app-temp/data/validation/runs/2026-09-29-note-coverage/review-coverage.json)、[browser results](../app-temp/web/output/playwright/note-coverage/results.json)、[桌面](../app-temp/web/output/playwright/note-coverage/coverage-gap-revealed.png)、[窄屏 Notes](../app-temp/web/output/playwright/note-coverage/coverage-narrow.png)。

重現指令（預設離線；已保存結果會重用）：

```powershell
dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -o app-temp/data/validation/note-coverage-build
dotnet app-temp/data/validation/note-coverage-build/Playback.Checks.dll --notes-redesign-check
node app-temp/checks/run-note-coverage.mjs
# web 目錄：
node src/test/note-coverage-check.mjs
```

經明確批准後，`node --env-file-if-exists=.env app-temp/checks/run-note-coverage.mjs --live` 最多三次 Gemini 文字生成，使用 production NoteAgent 及隔離 memory store，保存 usage、版本與未引用餘額；不新增 ASR、上傳音訊或改原 Mongo session。新的付費實驗須用 `--folder` 指定新 run 資料夾並提供其輸入快照，避免覆蓋已付費結果。完整自然全堂重建及內容品質仍需另行驗收。
