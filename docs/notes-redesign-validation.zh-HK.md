# 2026-09-29：筆記重構實作及驗收

這是初版驗收紀錄。後續已加入逐 record 查詢、完成段落合併、interim ASR execution、loading／Mermaid 延後載入及失敗 usage；一小時長測、最新 build 和未通過的 DB／provider E2E 見 [Week 3 報告](week3-one-hour-validation.zh-HK.md)。下方「未實作」及 live 授權計畫是初版當時狀態，以後續報告為準。

程式已加入 section 更新、10 秒 Jev note gate、Reading／Sources、section 整理、group flow、詳解及逐字稿 virtualization。以下區分離線行為、保存資料的人工核對，以及尚未完成的真實 provider／MongoDB／長時間驗收。沒有 commit／push、讀寫 `.env`、清空 DB、啟動容器或新增付費／音訊上傳測試；原有 WIP 保留。

後續以 **frontend memory 為優先**，重現並修復 Mermaid 錯誤 DOM 累積、上一堂引用索引 retention、Activity 配置及錯誤 scroll parent；10 分鐘 fixture完成，逐項原始配對／retained path／限制見 [frontend memory 報告](frontend-memory-validation.zh-HK.md)。下面較早 dev 短測摘要不能取代這次 production 配對；三小時 wall-clock 仍未驗證。

**Live runtime 未更新。** 最後只讀核對 5078 無法連線，沒有重啟它。隔離 API 5079 用 validation build、`PLAYBACK_OFFLINE_TEST=yes`、自動 notes／terms／organize 及 saved-ASR recovery 關閉，驗證 health／capture 後已停止；MongoDB unavailable，capture idle。這不是正式 runtime 驗收，也沒有套用恢復 preview。後端變更需正常重啟 API 才生效；若仍有錄音，先在 UI 正常 Stop，再在原 dev 終端重啟 `pnpm.cmd dev`。

最後隔離build為`d222fb3a-eb3c-4571-99ac-ba84c14c0f46`，`startedAt=2026-09-28T17:12:36.5978211Z`（香港9月29日01:12）；health的`sectionNotes=true`、`sessionSync=true`、interval10、prompt`section-notes-v1`，`mongo=false`。保存於 [offline-health.json](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/offline-health.json)。`jevNoteGate`只反映credential配置，不代表live品質成功。

## 逐項結果

| 原問題／要求 | 結果 | 具體證據與限制 |
|---|---|---|
| 舊內容消失、metadata 冒充 coverage | 已實作及離線驗證；歷史恢复未套用 | [NoteSections](../app-temp/api/Services/Ai/NoteSections.cs)、[SectionNotes](../app-temp/api/Db/SectionNotes.cs)：整份 replacement 已移除；只更新選定 sections，未修改 section 保留。每個來源連到實際寫入的 point；只登記 source ID 不可完成。一般更新會保存模型省略的原 point 文字，保護公式／例子；user edit／delete 有保護及 suppression。獨立整理仍需核對重寫後的語意，point IDs／provenance 不是語意正確性證明。 |
| v112→v113 與更早遺漏、可審閱恢復 | 已生成只讀 preview；全堂及更早版本未驗證 | [合併閱讀 preview](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/readable-preview.md) 保留 v118，附加 v112 五個候選 section；[原 ASR 抽樣核對](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/source-review.md) 含公式／條件／例子及四個有來源的關係圖。只有 v19–v118 快照；v1–v18、全堂逐句遺漏未核對。 |
| History 超過 100 版不可讀／restore | 已實作；UI 分頁離線驗證；DB 整合未驗證 | `GET notes/history?before=&limit=`（最多50）、`GET notes/{version}` 直接查版；restore／recovery 保存新版本、有 base guard。舊 `GET notes` 的最近100版相容入口仍存在，新 UI 使用分頁及直達。fixture 120版分頁至v20、直讀v1；真實Mongo125版測試因連線失敗未執行寫入。 |
| Jev 每 10 秒決定筆記 | 已實作及離線驗證；provider 品質未驗證 | [NoteScheduler](../app-temp/api/Services/Ai/NoteScheduler.cs)、[NoteAgent](../app-temp/api/Services/Ai/Agents/NoteAgent.cs)、[JevNoteGate](../app-temp/api/Services/Ai/Providers/JevNoteGate.cs)。可控 clock：9.9秒無呼叫，10秒一次 note 專用 Jev→LLM；idle零provider；wait累積、不重評 unchanged input；允許立即排入生成。不是固定每10秒生成，更不保證10秒內完成。 |
| 慢／失敗／重啟／Stop／manual／競態 | 已實作及離線驗證；真實重啟持久化未驗證 | 每session去重、4個自動工作及4個生成slots、round-robin admission；慢session不阻塞其他有空slot的session。共用 [JevTransport](../app-temp/api/Services/Ai/Providers/JevTransport.cs) 的model discovery／HTTP，術語與note任務各自限制，30秒transport deadline。失敗最多3次退避；LLM重試重用已允許gate；Stop保留flush版本並等待late final；manual立即。保存wait identity的restart測試用memory store；Mongo持久化／併發flush測試未驗證。 |
| 永遠 wait／deferred 窗口阻塞後段 | 已實作及離線驗證；自然內容品質未驗證 | 3次changed-input wait後，累積≥2000字或超過窗口的backlog可觸發明確標示 safeguard；Stop先詢問Jev，再可用stop safeguard。沒有把wait偽裝allow。Deferred片段帶附近完整原文context，新的完整來源可前進；未見來源不會標completed。全部deferred不保存空版本；未變Stop input不重付費。 |
| VAD 太碎、數學／例子／論證易懂 | 結構及prompt已實作；自然生成品質未驗證 | pending依時間累積同session原文、攜帶source identity；LLM負責語意組合，Jev只決策。稳定section／point IDs、late ASR及explicit deferred；不以VAD當topic。prompt取消固定2–4bullets／30words，要求定義、符號、條件、推導、例子、論點及有用圖表。Fixture及人工ASR閱讀樣例不能證明模型會照做；也未驗完整語意compression。 |
| 引用太多、約五分鐘來源入口 | 已實作及離線/UI驗證 | [NotePreview](../app-temp/web/src/pages/NotePreview.tsx)、[SourceCitation](../app-temp/web/src/Component/SourceCitation.tsx)：Reading收起來源；Sources每section一入口，按source及≤300秒display ranges分組；popup列精確集合、間隙、原文、wall clock與elapsed time及播放。切換只改呈現，不呼叫模型。測早／晚未掛載來源、分開的80分鐘來源、combined audio依序播放及unknown unavailable；原chunk IDs不重新編號。 |
| 長引用 ID 及 prompt成本 | 短引用已實作；真實費用未驗證 | `cite_` identity由session＋排序後的精確source集合hash生成並保存；完整provenance在同一note version。新prompt用有界source aliases，持久化不依prompt-local編號。materials按≤3000字穩定passage ID分段，原文不改。序列化input≤48000字元／輸出8192tokens；drop整個optional context，沒有截斷來源。超大單段顯示失敗。尚未取得真實新provider payload／completion usage，不能由舊88.8%儲存比例推出費用。 |
| 高亮詳解與普通詞 | 已實作及UI驗證；長詳解品質未驗證 | `term-detail-v2`：保存短summary＋完整explanation、3072-token cap、定義／課堂用途／例子／數學／關係／限制及適合圖表；明示AI/web supplement，舊解釋明確detail操作才升級，保留舊內容history。hover／render讀保存內容；fixture展開full explanation零POST。`details`等普通詞排除。真實搜尋結果、相關性、圖表及詞語precision/recall未測。 |
| 指定section的獨立整理 | 已實作及離線行為驗證；自然compression未驗證 | `section-organize-v1`真正在生成request中，selected section／base版本／來源與未修改sections共用同一note/history。可明確整理protected section；自動策略預設關，`PLAYBACK_AUTO_ORGANIZE=yes`每30分鐘選長／重複的未整理、非user-edited section。有界input超限會真實失敗，不偷偷改成空context。測prompt／usage／sectionId、公式、其餘section、stale及concurrent base rejection。 |
| Group … 70% flow modal | 已實作及UI驗證；真實DB executions未驗證 | [AiFlow](../app-temp/api/Services/Ai/AiFlow.cs) 共用runtime prompt常數／配置；[GroupFlow](../app-temp/web/src/pages/GroupFlow.tsx) 約70vw、窄屏、native focus／Escape、固定close、loading／failure／retry／empty、group/session遲到結果隔離。配置graph與已保存execution分開；gate的wait／error／retry／flush可讀。GET不生成。Confirmed ASR、notes、organizer、ranking、explanation、translation、chat記錄prompt/hash/model/input identity、可用usage及latency；live interim ASR provider calls尚未接入execution紀錄，沒有補造舊紀錄。 |
| 長逐字稿／反覆編輯記憶體 | 已修復重現路徑及10分鐘fixture驗證；逐record DB／新定期合併未實作；三小時未驗證 | [專項報告](frontend-memory-validation.zh-HK.md)：真正scroll parent及wheel、interim可見列、Mermaid自有host清理／byte-bound cache、session引用索引釋放、lazy detail bodies、API timer/listener dispose／read abort及unselected-recording讀取guard。2,700＋292 finals／440cycles，定期GC約15–16.4MiB，切小session舊source IDs 0。增量sync仍≤128 records／約400KB/頁；**server changed revision仍查整個Session／重算fingerprints／terms，不是逐record DB增量。**原始ASR及identity不改，新定期展示合併未實作。 |
| 未保存 editor／caret 的背景更新 | 已實作及UI驗證 | editor保留實際base版本，背景新note不會把base偷偷前移。衝突顯示current saved notes並停止Save／Revise；可明確load最新，保留一份本session舊draft供recover／copy。整理／restore／apply recovery於dirty editor停用；無自動丟稿。未做三方自動文字merge；使用者可審閱後整合，note版本仍完整。 |

## 歷史與來源核對

[history-coverage.json](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/history-coverage.json) 記錄每版hash、metadata與正文真正引用的來源。v103與v110 hash相同，registered sources由533增至611、正文引用332不變；v112引用356、v113只有34、v118只有50。這是traceability核對，沒有把每個cited ID當成完整消化的證明。

恢復preview有5個歷史候選＋1個current section；[merged-preview.md](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/merged-preview.md) 保存原措辭，[readable-preview.md](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/readable-preview.md) 用短來源標籤供閱讀，[citation-provenance.json](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/citation-provenance.json) 保留完整IDs／時間／原ASR。155個入口中5個含不可用歷史ID，沒有猜修正。

[source-review.json](../app-temp/data/validation/runs/2026-09-28-notes-retention-125936/recovery-preview/source-review.json) 的7個主題窗口共95個來源parts涵蓋公平分享、TCP與該目標的關係、speed-test範圍、安全、分層、五層職責、封裝／解封裝。R/n是依平均分享原話整理的符號目標，n=10→R/10有明確原話；不是實際throughput保證。Wireshark名稱有ASR疑點；quantum／botnet等其他歷史段落尚未在這個抽樣核對。Repo全堂transcript.txt內容與這個networking錄音不一致，不能拿來當對齊證據。

## 測試與效能證據

隔離輸出沒有覆蓋正常API DLL：

```powershell
dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -p:BaseOutputPath=C:/Users/peter/Documents/Github/playback/app-temp/data/validation/build/ -p:UseSharedCompilation=false
dotnet app-temp/data/validation/build/Debug/net10.0/Playback.Checks.dll
dotnet app-temp/data/validation/build/Debug/net10.0/Playback.Checks.dll --notes-redesign-check
dotnet app-temp/data/validation/build/Debug/net10.0/Playback.Checks.dll --notes-recovery-preview app-temp/data/validation/runs/2026-09-28-notes-retention-125936
npm.cmd --prefix app-temp/web run build
```

.NET build零warning／error；原protocol／ASR／real-PCM streaming fixture／language／output truncation checks及新notes redesign checks通過，provider transport全部為memory HTTP。新check實際執行NoteAgent，不只是斷言interval常數。2,700 sources＋200,000字中文note同步用25頁／51個bounded fragments；idle=0 records、單一來源改動=1 record、跨session cursor reset。

Mongo的 `--notes-store-check` 只指向localhost `playback_e2e`，會保留本次測試資料以便審閱；本輪因Mongo unavailable返回NOT VERIFIED，沒有做DB寫入。不能把memory-store checks說成Mongo分頁／restore／flush race已通過。

以下從 `app-temp/web` 執行；另開已安裝Vite於5174，不啟動live API：

```powershell
node src/test/notes-chat-check.mjs
$env:PLAYBACK_OFFLINE_TEST='yes'; node src/test/asr-ui-check.mjs
node src/test/notes-redesign-check.mjs
node src/test/notes-snapshot-performance.mjs
```

Browser為headless Edge 154.0.4258.37、Vite dev、API fixture攔截，外部HTTPS封鎖；無付費請求或使用者browser操作。既有notes/chat／ASR checks已按新的Reading／Sources及virtual rows更新，保留播放、retry、language、chat failure、viewport等行為核對。新fixture讀history／recovery／flow／saved full explanation均零POST、零page errors；測unsaved draft／caret、並行新note衝突及舊draft可恢復、1,350／2,700mic＋system身份、跨三小時、125%zoom及760px，既有fixture另涵蓋320–1440px。已目視Reading、Sources、full explanation、history recovery、flow、zoom及窄屏截圖。

| 重播情境 | Mounted rows／DOM elements | GC後heap |
|---|---|---|
| 同一737 transcripts／745 chunks舊快照，舊audit baseline | 738／17,210 | 46.35MiB；舊audit末段48.92MiB |
| 同一737快照，目前idle | 14／961 | 約14.9MiB |
| 同一737快照，24次Edit／Preview＋interim，settled | 14／962 | 約16.8MiB；listeners回到547 |
| 新1,350 mic＋system fixture，最新production idle | 11／514 | 約8.53MiB |
| 新2,700 mic＋system fixture，最新production 8cycles／96edits | 11／514 | 約12.45MiB |
| 最新production切回小session | 0／316 | 約10.03MiB |

數值詳見 [最新production長列表metrics](../app-temp/web/output/playwright/notes-redesign/metrics.json) 及 [較早dev同一737快照metrics](../app-temp/web/output/playwright/notes-redesign/snapshot-737-metrics.json)，含GC前後bytes、script/task/layout CPU時間、DOM counters／listeners、payload及耗時；bytes轉MiB除1,048,576。舊audit字段叫MB，實際亦除1,048,576。**表內mixed dev／production不能直接當修復改善的配對；嚴格配對另在frontend專項報告。**當時HEAD dev baseline啟動失敗；本次frontend用修復前WIP production build另做before／after。舊2700audit沒有相應mic＋system audio，不能當成同一新fixture的精確配對。

後續已做隔離browser retained paths／heap snapshots及10分鐘持續編輯／confirmed-interim更新／session切換；原browser／三小時真實錄音、長語音及多次pause/resume全組合仍未驗證。Mermaid目前cache≤32／≤2MiB、pending≤8，取消未開始而無讀者的工作，自有render host在finally清除；已開始工作仍不能硬取消。Web build仍提示約1.3MB主bundle及Zod annotation warning，未做bundle拆分；API wire／DB查詢限制見frontend專項報告。

## Provider核對與下一次有限驗證

共享transport及note專用`noul`／`choice`問題按 [TypeSafe OpenAPI](https://api.typesafe.ai/openapi.json) schema實作；runtime用GET models discovery，不硬寫猜測model。生成仍用 [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/) 及 [Google GenAI .NET SDK](https://googleapis.github.io/dotnet-genai/)。實際request prompt版本：`note-gate-v1`、`section-notes-v1`、`section-organize-v1`、`term-detail-v2`；Activity保存生效prompt、hash、輸入身份、base、source集合與provider確有回報的usage，沒有保存credential或私有reasoning。

沒有適用於新note gate／section prompt的付費live授權。完成Mongo恢復連線及runtime重啟後，有限文本驗證計畫是：

1. 自建保留的E2E session，離線核對125版分頁／v1直讀／restore新版本、恢復候選選擇／base guard、Stop concurrent flush，保留結果；不清空其他資料。
2. 明確live opt-in後，只用上面已對齊ASR文字，**不上傳音訊**：3次Jev（半句、完整公平分享例子、追加安全／換題）；2次section notes（早段＋新章節）、1次指定section整理、1次術語詳解，共最多7次高層provider工作。模型discovery及grounded search可能另有HTTP requests，全部記錄實際usage／latency／response／失敗；失敗不自動加碼重跑。
3. 對保存結果人工逐项檢查R/n／R/10及同一bottleneck條件、完整例子、speed-test範圍、安全、五層／封裝、舊內容與user fact保留、來源錯綁、推測／幻覺、圖表增益及詳解實質補充。這些才是自然模型品質驗收，不能由手寫fixture替代。
4. 保存結果後重播UI，不重付費；以同一fixture／browser／build做較長interim、編輯、popup、diagram、播放、session切換與heap retained-object核對。另補真正DB增量查詢及定期transcript display consolidation；資料身份／原始ASR不變。

先前handoff的研究快照及未實作狀態見 [原交接](notes-redesign-handoff.zh-HK.md)，目前驗收以本文件及其逐項限制為準。
