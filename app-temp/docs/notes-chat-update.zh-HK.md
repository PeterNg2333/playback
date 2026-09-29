# 筆記、引用、流程及長列表的目前行為

本文件描述 2026-09-29 程式。初版結果見 [筆記重構驗收](../../docs/notes-redesign-validation.zh-HK.md)，後續逐 record 同步、完成段落合併、loading、成本紀錄及一小時測試見 [Week 3 驗收](../../docs/week3-one-hour-validation.zh-HK.md)。較早的固定 90 秒、全篇 replacement 及 768-token 詞解釋描述已被取代。

## 內容與版本

同一 note/history 包含穩定 section、實際 point、coverage 和持久化 citation。NoteAgent 只更新指定 sections 或新增主題，程式保留其餘 sections；來源 metadata 本身不能完成 coverage。普通更新保留原 point 文字、公式、條件、例子及 provenance；使用者編輯受保護，刻意刪除記入 suppression，不自動復活。獨立整理可重組文字，仍需核對意思。

`section-notes-v6` 使用同一 `NotePatch` 衍生的 SDK JSON schema：每個 `points[].text` 就是实际 Markdown 正文，沒有另請模型重寫 `markdown`。程式從 points 組合 section，依 `sourceIds` 驗證及產生持久化短引用，並從輸入捕捉 section version；不讓模型重複輸出 concurrency version。新 section 的 id 必須是 `new`，existing id 限於實際可更新集合。

有效 partial patch 可保存；明確 deferred 保留原因，沒有交代的 input 保持 pending，不會被當成 completed。最多64個來源／18,000字來源正文，整個序列化 input 仍有48,000字元限制。Coverage 是正文與來源對應，並非語意正確性評分；自然 Week 3 結果仍有錯誤、不完整及欠缺圖表，見人工核對報告。

每次保存成新版本並核對 base。歷史每頁最多 50 版，也可按版本直接讀取；restore／recovery 建立新版本。Recovery 先 preview，沒有自動套用到 live notes。Live draft 未保存、引用未驗證。背景生成不會偷偷前移 editor 的 base；衝突可見並保留舊稿、caret 和閱讀位置。載入 session 時顯示 Loading session 並停用 Save；讀取失敗保留未保存稿，明確重試才切換。

## 10 秒 Jev note 決策

NoteScheduler 每 10 秒檢查 changed confirmed ASR／教材。這是決策週期，不是生成時限。JevNoteGate 使用 note 專用 `noul`／`choice` 問題，術語 ranking 不當作筆記 probability。允許後立即排入 section LLM；wait 保留 pending；idle／相同未變輸入不重付費。

每 session 去重，四個自動工作及四個生成 slots；慢工作在有空 slot 時不阻塞其他 session。共享有界 Jev transport／discovery，note 和 term 有各自任務限制。錯誤最多三次退避，成功 allow 可供 LLM 重試重用。Stop 先再決策，必要時保存明確標示的 flush safeguard；manual Revise 即時。Clock／memory 測試覆蓋 deferred、restart、edit／language 競態；Mongo 檢查亦通過 note gate／Stop flag、歷史／restore，以及 API 重啟後的保存資料與 cursor reset。

語意消化由生成模型負責。VAD 是傳輸邊界，原始 ASR／錄音身份保留。Prompt 要求來源支持的定義、符號／條件／推導、標示例子、論點／理由／結論，以及有用的 table／hierarchy／Mermaid。沒有固定每題 2–4 bullets／30 words；不得填造缺失的定義或數學。

## 閱讀、來源及詞解釋

Reading 收起引用，Sources 每 section 提供短入口；按 source 及最多 300 秒 display ranges 聚合。展開展示精確引用集合、間隙、原文、時間及個別／組合音訊，沒有把未引用 chunk 當證據。未知來源顯示 unavailable。`cite_` identity 按 session／精確 source 集合產生，完整 provenance 隨版本保存，不依 prompt-local alias 編號。

高亮 popup 讀保存短 summary，Read full explanation 展示保存詳解，hover／render 不呼叫 provider。`term-detail-v2` 要求定義、課堂用途、來源支持的例子／符號／限制／關係及適合表圖，明示 AI/web supplement。普通詞如 details 被排除。解釋存於 `term_insights`，不新增長筆記段落或 note version。舊版需明確 detail 操作升級，並保存歷史。

## 整理及 Group flow

指定 section 整理使用獨立的 `section-organize-v6` 生效 prompt、同一 note/history 及 base guard，其餘 sections 保留。可選自動策略 `PLAYBACK_AUTO_ORGANIZE=yes` 預設關，每 30 分鐘挑長／重複、非 user-edited section。一次 v4 真實整理因模型回傳錯誤 section version 被拒絕；現在版本由程式捕捉，離線 stale／concurrent checks 通過，尚未追加真實整理重試。

Group … → View AI flow 開啟約 70vw modal，窄屏適配、keyboard／Escape、loading／failure／retry／empty 可見。配置與實際 executions 分開；group 是查看範圍，來源按 session 隔離。圖及列表共用 runtime prompt／模型／trigger 常數，GET 不生成。

Activity 保存生效 prompt／hash、input identity／bytes、model、base／section、queue／schedule／provider latency、lifecycle 和 provider 有回報的 usage。REST ASR interim 亦記錄實際參數、PCM hash、preview 角色及 usage，明示沒有傳 system prompt。原生 realtime provider 尚未接入，沒有補造歷史。

Notes polling 使用 `activity?includePrompt=false`，省掉不使用的完整 prompt，保留 draft／hash／sources／usage／lifecycle。Group flow 仍讀完整生效 prompt，Mongo 中保存內容不被刪除或改寫。這次 fixture 每筆平均傳輸由約 742 KB 降到 58 KB；是 UI wire bytes，不是 provider 費用。

## 前端及同步

`VirtualTranscript` 只掛載 visible rows／overscan，使用 `.transcript-content` 捲動，支援可變高度、來源 reveal、day/hour、interim 及原文展開。Combine completed passages 合併同 source 的成熟確認段：最多 120 秒／16 parts／3200 字，保留 gap／overlap 限制和原始 IDs／音訊／選取。這是展示合併，沒有聲稱時間拼接等於語意摘要。

`SessionSync` bootstrap／ASR 顯示語言重設仍讀一次完整 Session，之後依 journal exact IDs 查 transcript／chunk／material／insight。Translation／note-status 不重掃全部術語；新來源只更新受影響 term 及已知 explanation provenance。Idle 零 record queries。每頁最多 128 records／約 400 KB，大 record 以 bounded fragments 組合，單筆最多 800 萬字元。Cursor 是記憶體內最多 32 個 reader 狀態（閒置 5 分鐘），重啟／過期／journal 落後會 reset bootstrap，沒有永久 DB cursor。

Mermaid 首次需要圖表才載入，顯示 Loading diagram。自有 render host 在 finally 清理；cache 最多 32 項／2 MiB，pending 最多八個，取消未開始且無讀者的工作。語法失敗只存小標記，renderer 載入失敗不當語法錯誤缓存。拆分後仍有大於 500 KB 的 chunk warning。

## 模型及成本

Routine 預設 `gemini-3.1-flash-lite`，environment allowlist 另容許 3.5 Flash-Lite，health 顯示實際模型。沒有額外 advisor。Section notes／organizer 上限 8192 tokens、translation 4096、普通 Q&A 1024、grounded term 3072。Token-limit completion 拒絕保存，仍收集 provider 回報 usage，包括 SDK finish reason 後的 usage 和 grounding 驗證失敗。

沒有 explicit context cache resource；只有 provider usage 回報 cached input 才列相應 token 估價。相同 Q&A context 可按既有期限本機重用，失敗不進成功 cache。Usage 缺失不計零。離線報告合併初次 provider run 與 Mongo activity，按 execution id 去重，包含失敗請求；validation errors 不冒充額外呼叫。按 [Vertex 定價](https://cloud.google.com/vertex-ai/generative-ai/pricing)／[TypeSafe 模型定價](https://docs.typesafe.ai/models) 列標準／cached input token估價及保存Search queries的全額計費情境。帳戶剩餘Search quota、未報usage、credits和地區差額未知，不是帳單。

## 驗證入口

```powershell
dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -o app-temp/data/validation/build -p:UseSharedCompilation=false
dotnet app-temp/data/validation/build/Playback.Checks.dll
dotnet app-temp/data/validation/build/Playback.Checks.dll --notes-redesign-check
# 有 localhost MongoDB 才建立並保留 playback_e2e 測試資料
dotnet app-temp/data/validation/build/Playback.Checks.dll --notes-store-check
dotnet app-temp/data/validation/build/Playback.Checks.dll --session-sync-store-check
dotnet build app-temp/api/Playback.Api.csproj --no-restore -o app-temp/api/bin/week3-validation/net10.0 -p:UseSharedCompilation=false
npm.cmd --prefix app-temp/web run build
```

瀏覽器測試從 `app-temp/web` 執行，production preview 5174 或用 `PLAYBACK_WEB_TEST_URL` 指定 localhost port，設 `PLAYBACK_UI_PRODUCTION=yes`。Notes／chat／ASR／loading 攔截 API、封鎖外部 HTTPS，不能當真實 DB／provider／硬件 E2E。

`pnpm.cmd test:week3-provider` 預設離線，明確 `--live` 才執行有限文本測試，保存結果重用、失敗返回非零。新 `--refresh` 呼叫需適用授權。`pnpm.cmd validation:week3-cost` 預設只讀保存檔；`week3-report.mjs --snapshot` 僅 GET 隔離 localhost API。批准後已取得真實 Jev／Gemini usage 和 Mongo E2E；完整自然筆記品質仍未通過。沒有清空資料庫、提交或 push。
