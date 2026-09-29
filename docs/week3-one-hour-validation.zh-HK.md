# 2026-09-29：Week 3、一小時 frontend load、loading、cost 及 E2E

使用者要求先完成交接功能，load 用一小時，使用 repo Week 3 transcription，並核對 frontend loading、cost 和完整端到端。既有 section／Jev／Reading／organizer／flow／詳解／virtualization WIP 保留，本輪補上剩餘實作；沒有 commit／push、清空 DB 或向 provider 上傳音訊。

**一小時前端 fixture、Mongo 保存／歷史／重啟，以及主要 browser→API→DB E2E 已通過。** 使用者其後批准啟動 repo Mongo 和有限文字 provider 測試，Jev／Gemini 真實結果與22筆 usage 已保存。九次筆記生成有六次失敗、三版保存；42條來源有 written points、65條 deferred、30條 pending。自然筆記仍有不確定性與內容／圖表問題，**完整自然品質及修正後 organizer 尚未通過**。追加生成的次數授權尚待回覆，沒有超過原九次上限。

## 實作及驗收範圍

| 功能 | 已完成的實作／驗證 | 限制 |
|---|---|---|
| section 保留、coverage、歷史／恢復 | Patch指定section；canonical point正文與程式產生citation；partial缺失保持pending。Mongo125版分頁、v1直讀、restore成v126、citation／stale guard／Stop flag通過；重啟後仍有126版。三版自然筆記保留早版已寫point | 未套用原錄音歷史preview；引用身份／保留不等於語意正確 |
| 10 秒 Jev gate | Clock測9.9秒零call、10秒allow→LLM及競態通過；真實完整內容allow、碎句wait，executions保存決策及usage | 有限決策樣本，非一般品質評分；10秒是檢查周期 |
| ASR 語意筆記／有用圖表 | 生效 section prompt 要求定義、符號／條件／推導、例子、論點及有用表圖；原 ASR 不改 | 自然內容遵循程度與幻覺未通過 |
| Reading／Sources、詳解 | 每 section 短入口、最多 300 秒 display range、精確 provenance／unknown unavailable／早晚來源 jump；hover／full detail 只讀 | fixture 通過不等於詳解自然品質通過 |
| section organizer／group flow | Group flow由真實Mongo executions讀到prompt/hash/input/usage；重啟後仍可讀，GET零provider calls。整理的selected section／stale／concurrent guard通過 | 一次自然v4整理因模型錯誤section version被拒絕；已改成程式捕捉version，尚未付費重試 |
| **逐 record 同步** | [SessionSync](../app-temp/api/Db/SessionSync.cs)按changed IDs查詢；Mongo900來源／15頁bootstrap、idle零queries、exact translation/chunk/statusdelta、舊explanation新增教材provenance通過。實際API重啟前cursor於重啟後reset、15頁重載、idle零changes | Bootstrap／display-language重設仍讀完整Session；RAM journal／cursor非永久DBcursor |
| **完成 transcript 展示合併** | [transcriptPassages](../app-temp/web/src/pages/transcriptPassages.ts) 同 source、成熟確認段，最多 120 秒／16 parts／3200 字，保留 gap／overlap 限制、original IDs、翻譯／音訊／selection。可 toggle。2,700 原 IDs 保留、display rows 減少、來源 jump 展開正確 | 是展示合併，沒有把時間 join 當 semantic compression |
| **session／diagram loading** | 慢startup／switch有status、Save停用、503保留draft／retry。Mermaid延後載入；修正TranscriptContent漏比較workspaceLoading及材料chooser的DOM接線。真實切堂／材料上傳及最後cold／loading checks通過 | Cold asset數字仍是fixture，非真實DB／provider SLA |
| **usage／ASR execution** | REST interim 保存 input hash／PCM identity／model／prompt角色／usage／latency。SDK 截斷仍 drain 遲到 usage，grounding 驗證失敗仍保存報告 usage，失敗 latency 亦記錄 | 原生 realtime provider／硬件新長測未執行；unknown usage 不當零 |
| **一小時 load** | 固定 production build，1,689 cycles、900→2,600 原 IDs；visible rows 1–16、GC heap 12.78–15.42 MiB、small-session 舊 source IDs=0 | notes 是 performance fixture，並非模型生成品質證據；未驗 whole-browser RSS |

## 輸入及 build 身份

來源為 [repo transcript.txt](../app-temp/data/test-audio/sampleAudio/transcript.txt)，SHA-256（讀取文本）`21f256c8b1b93cc5558ea2cca601efd76f159091705b581c87780bbd50c2628f`。首一小時有 409 個 timed passages；完整文本是 OS／kernel debugging 內容，與先前恢復 preview 的 networking 錄音不同。沒有把舊 R/n／五層案例強行套入這份課堂。

一小時 UI 工作量最初 900 個 dual-source 8 秒原始列；精確 Week 3 文字被重取樣到兩條時間線。每約 4 秒加入兩個 final，並有 interim／100 activity／100 edit-history／24 synthetic note sections；這是性能資料，**不是新的 ASR 對齊或自然筆記評分**。40 段首 20 分鐘音訊沒有再上傳。

一小時 soak 使用 `index-B4gBMhDZ.js` 固定 build。其後 loading 最佳化的隔離 build 為 `index-D6pC8kzE.js`，在另一 port 驗證。長測不混換 build；最後 build 的 loading、Notes／chat／ASR／失敗重試／125%／窄屏、2,700 原 IDs 和短自然資料 soak 另有通過證據。

Compact stage `index-DS97T6wV.js` 另補兩項核對：renderer 載入失敗與語法錯誤分開，提示先保存／複製 draft，再 reload（browser 會保留失敗 module，Reopen Preview 無效）；該恢復流程已通過。Notes Activity polling 改為 `includePrompt=false`，保留 draft／identity／hash／usage／sources／lifecycle，完整 prompt 在 storage 和 Group flow 保留，不會因投影而清掉。

## Frontend loading

冷 browser、production preview、cache 關閉、模擬 4 Mbps／80 ms 資產網絡，API 為即時離線 fixture；同一台電腦正在跑長測，以下是單次比較。

| 指標 | 修改前 | Mermaid 延後載入 |
|---|---:|---:|
| 首次 JS encoded bytes | 359,777 | 198,184（少 44.9%） |
| DOMContentLoaded | 1,102 ms | 718 ms |
| First contentful paint | 1,372 ms | 1,016 ms |
| app 可用 | 1,497 ms | 1,077 ms |
| 首次打開 flowchart | 959 ms | 1,436 ms |

減少首屏下載／解析；第一個圖表要額外載入 renderer。已有圖表的長文仍會在需要時載入。不是實際網絡／API SLA，也不是統計顯著的多次 benchmark。拆分後 entry 約 644 KB／gzip 198 KB；Mermaid core 約 666 KB／gzip 161 KB；仍有 >500 KB chunk warning及既有 Zod annotation warning。

原一小時 fixture 的 Activity 回應含 100 個完整 prompt，造成約 3.49 GB 重複文字傳輸，雖 heap 可回收仍是成本問題。新增 compact 回應後，同一個 900-row／100-activity／24-section 短 fixture，每次 long-session Activity 平均由 **741,680 bytes 降至 58,187 bytes（少 92.2%）**。保存的 [before](../app-temp/web/output/playwright/frontend-memory/week3-final-smoke/summary.json)／[after](../app-temp/web/output/playwright/frontend-memory/week3-compact-activity/summary.json) 時段長度不同，因此比較每次平均、不比較總量或推斷實際帳單。Backend serializer 檢查亦驗證不傳10萬字prompt、仍有draft/hash/source，原record及完整投影沒有丟prompt；後續真實Mongo Group flow及restart readback亦通過。Activity仍重取最多100筆metadata／live draft，並非完整活動DB增量cursor。

原始 [before](../app-temp/web/output/playwright/frontend-loading/before-lazy/results.json)／[after](../app-temp/web/output/playwright/frontend-loading/after-lazy/results.json)、[loading 失敗／retry](../app-temp/web/output/playwright/session-loading/results.json)。最後 build 的完整 frontend fixture [metrics](../app-temp/web/output/playwright/notes-redesign/metrics.json)：2,700 原始身份，GC 後 idle 11.64 MiB、8 次 edit／preview 後 12.14 MiB、小 session 10.09 MiB；零寫入及 page errors。較早 lazy build 的 Week 3 短測 [結果](../app-temp/web/output/playwright/frontend-memory/week3-lazy-smoke/results.json) 同樣零 page errors／Mermaid 殘留，切小堂 GC 10.55 MiB。

## 一小時長測

已完成 3,600 秒持續操作（含結尾核對的記錄時間 3,611.255 秒），Edge 154.0.4258.37、production preview、外部 HTTPS 封鎖。1,689 次 edit／preview／diagram／source-popup 循環，約每七次切小堂再返回；每 20 次有 125%／760px screenshot。初始 900 原始來源，加 1,700 個 mic＋system finals，最後 2,600 原 IDs。所有原 ASR／source identity 保留。

| 指標 | 實測 |
|---|---:|
| 定期 GC 後 heap | 12.78–15.42 MiB；median 14.38 |
| 最初／最後五個 GC median | 13.15／14.87 MiB（來源數同時增加） |
| Mounted visible rows | 1–16；median 12 |
| Soak DOM elements | 780–1,062；median 976 |
| Mermaid orphan render hosts | 0 |
| Renderer main-thread task／script／layout time ÷ wall time | 49.87%／4.84%／4.72% |
| 切回 small session | heap 12.24 MiB；0 rows／215 elements |
| small-session 旧 source ID strings | 0（heap snapshot） |
| small-session documents／DOM nodes／listeners／AbortSignal | 2／647／392／74 |
| 未選中錄音堂的多餘 sync requests | 0 |
| Page errors／POST writes | 0／0 |

Task time 代表這個頻繁編輯／圖表／resize fixture 的 renderer main-thread 佔用，包含 automation／profiling，沒有把它當 process CPU 或 idle baseline。Script／layout 是其中可分類的時間，不是總 CPU。Sync 約 252.8 MB 包含數百次切堂 bootstrap，不是普通 idle polling 的流量；Activity 原始約 3.49 GB，後續 compact 改善另測，未聲稱最後 build 也再跑了一小時。

[原始 results](../app-temp/web/output/playwright/frontend-memory/week3-one-hour/results.json)、[distilled summary](../app-temp/web/output/playwright/frontend-memory/week3-one-hour/summary.json)、[heap analysis](../app-temp/web/output/playwright/frontend-memory/week3-one-hour/heap-analysis.json) 及同目錄 screenshots／allocation profile／heap snapshots 保留。原 runner 有 legacy `fixtureDataHours=3` 及 `2700` label，與 `naturalInput.seconds=3600`／初始 900 不一致；raw 結果未改寫，summary 按實際 input 修正為一小時。程式已修正未來 run 的 label。

Heap snapshot 亦有 27,999 個 native NetworkResourcesData（self size 約 8.1 MB）、62,145 個 native MediaQuery。抽樣 MediaQuery root 在 Blink C++ CSSDefaultStyleSheets／RuleSet；不能以一個 sample 斷言所有 native growth 的原因或整個瀏覽器沒有 leak。JS 舊來源已釋放，Mermaid DOM 無殘留，**whole-browser RSS／native retention 的完整量度仍未通過**。

Compact stage `index-DS97T6wV.js` 的 Activity 短測另通過，small-session GC 10.55 MiB、old source IDs=0、零 page errors／寫入／Mermaid 殘留。最後 production Notes workflow／2,700 sources／group prompt／history／source reveal／125%／窄屏再次通過，小 session 10.06 MiB；這是該 stage 的 scoped evidence。

最終畫面核對另發現 760px 錄音 header 的 session title／controls 重疊、API 更新提示被 sidebar 遮住，以及 diagram failure 訊息受 pre whitespace 截斷。已用有界 grid／sidebar offset／文字 wrapping 修復。最後 build **`index-BSXnGpTx.js`／`index-D1vk5jlT.css`** 再跑 45 秒 Week 3 fixture，明確斷言 recording title 不與 controls 重疊、sidebar 不遮提示；ASR UI 全 viewport／old API compatibility 及 diagram 網絡失敗／保留稿／reload／文字無 clipping 均通過，亦已目視 [760px](../app-temp/web/output/playwright/frontend-memory/week3-final-layout/narrow.png)／[錯誤訊息](../app-temp/web/output/playwright/frontend-loading/final-layout-failure/renderer-load-failed.png)。[最後短測](../app-temp/web/output/playwright/frontend-memory/week3-final-layout/summary.json) small-session GC 10.95 MiB、old source IDs=0、zero writes/errors/orphan diagrams。

批准前階段的 API build `62c60b87-3c73-4c76-94f3-59e43d4438a7` 的 [health](../app-temp/data/validation/runs/2026-09-29-week3/offline-health.json)／capture為200、idle、interval10、mongo=false。當時停止隔離API／preview、沒有啟動容器。批准後才啟動Mongo，最新runtime及真實E2E見下節；上述一小時與各frontend stage的固定build證據沒有混換。

重播命令，從 `app-temp/web` 執行（已啟動固定 production preview）：

```powershell
$env:MEMORY_BASE_URL='http://127.0.0.1:5174'
$env:MEMORY_RUN_LABEL='week3-one-hour'
$env:MEMORY_SECONDS='3600'
$env:MEMORY_RAW_COUNT='900'
$env:MEMORY_WEEK3='yes'
$env:MEMORY_SECTIONS='24'
$env:MEMORY_ASSERT_BOUNDS='yes'
$env:MEMORY_NAVIGATION_CHECK='yes'
node src/test/frontend-memory-check.mjs
node src/test/inspect-browser-heap.mjs output/playwright/frontend-memory/week3-one-hour --assert-no-old-session
```

較早修復前／後的嚴格配對仍見 [10 分鐘報告](frontend-memory-validation.zh-HK.md)，未拿不同 build 的數值冒充該配對。

## 批准後的 API／Mongo／browser E2E

.NET build 零 warning／error；protocol／ASR／real PCM lifecycle／語言／SDK token cap／串流及非串流截斷／grounding rejected usage／來源 Q&A checks 通過。`--notes-redesign-check` 通過 retention／coverage／organizer／clock／races／bounded sync：2,700 sources＋20 萬字中文 note 分 25 頁、51 fragments，idle=0，單 ID delta 沒重讀 Session，跨 session reset。

Production browser 的 `notes-redesign-check`、`notes-chat-check`、`asr-ui-check`、`session-loading-check` 及 `frontend-loading-check` 通過。離線攔截 API，零 provider request／寫入；目視 QA 按保存截图核對。這涵蓋 UI workflow，不能取代真實 API＋Mongo＋provider E2E。

使用者回覆「我allow」後，啟動已存在的 repo Mongo container及volume，沒有pull新image／清空任何資料。所有新test資料只放在 `playback_e2e` 並保留；原5078未重啟。隔離API5081的自動notes／terms／organize及external ASR均關閉，只在明確有界phase呼叫provider；5079為offline API。

| 驗收 | 真實結果／證據 |
|---|---|
| Mongo notes store | `--notes-store-check` 通過125版完整分頁、v1直讀、restore成v126、persistent citation、stale base、concurrent Stop flag。保留session `dcab4418b845432db32ca5325627bfa6` |
| Mongo增量同步 | `--session-sync-store-check` 通過900 dual-source來源、15頁bootstrap、idle零record query、exact translation/chunk/statusdelta、舊explanation新增來源、重啟cursor reset。保留session `3de310ba23c741e38704367c3c83f753` |
| API restart readback | 重啟前取得真正cursor；重啟後該cursor reset、15頁bootstrap、idle零changes。Week 3正文／version／activity IDs和126版歷史／citation仍完整。[readback](../app-temp/data/validation/runs/2026-09-29-week3/mongo-restart-readback.json) |
| Session／group／material／note／audio | Production browser使用真實API/Mongo：create／rename／drag／nested group、文字材料chooser、note保存／reload、語言設定、silent WAV分類、mixed／source播放、Activity history和375px。Mic／system capture及external ASR未跑。[E2E](../app-temp/data/validation/runs/2026-09-29-week3/offline-e2e.json) |
| 自然notes與Reading／Sources／flow | 三版真實provider結果保存；早版point原文仍在後版，source popup精確來源、歷史v1及group的actual prompt/usage可讀。純讀取／重啟phase零POST。[read](../app-temp/data/validation/runs/2026-09-29-week3/browser-read.json)、[restart](../app-temp/data/validation/runs/2026-09-29-week3/browser-restart.json) |
| 私人聊天 | 真實NDJSONstream、同render重複send只有一個APIrequest、validatedcitation、conversation持久化、reload零額外providercall。375px長引用ID溢出已改短label，再用保存答案讀取驗證。[chat](../app-temp/data/validation/runs/2026-09-29-week3/browser-chat.json)、[read-only replay](../app-temp/data/validation/runs/2026-09-29-week3/browser-chat-read.json) |
| Public-web聊天 | 一次有界Q&A取得7個verifiable來源，lecture insufficient與web分開；usage保存。原browser run因test init注入sandbox iframe而失敗；限制top frame後原答案重播通過、source button target為HTTPS、zeroPOST。[original](../app-temp/data/validation/runs/2026-09-29-week3/browser-web.json)、[saved replay](../app-temp/data/validation/runs/2026-09-29-week3/browser-web-read.json) |

材料E2E亦發現並修正 `TranscriptContent` memo 漏比較workspaceLoading、chooser input未接DOM，以及test在舊session開details後才完成切堂的競態；測試明確等目標session完成。舊test翻譯label更新為實際介面的Enable translation。以上不是mocked API成功。

最後 frontend 為 **`index-sguJggMU.js`／`index-D1vk5jlT.css`**。最後 cold asset check 首次 JS 198,278 bytes、appReady 799.9 ms、DOMContentLoaded 672.9 ms、首次 diagram 864 ms；是新的單次 fixture，不把它與較早不同負載當配對性能提升。Delayed startup／503 保留稿／retry 通過。自動 notes 關閉時，pending 來源現在顯示「Sources awaiting revision」，不再誤示正在等自動決策；最後真實 Mongo read phase 亦斷言此狀態及實際 flow SVG，零 POST／page errors。[cold](../app-temp/web/output/playwright/frontend-loading/final-status/results.json)、[loading](../app-temp/web/output/playwright/session-loading/results.json)、[final read](../app-temp/data/validation/runs/2026-09-29-week3/browser-read.json)。

最後 validation API build `f8a3f63b-cadd-48a4-ba40-03f268185b38`、startedAt `2026-09-29T08:34:16.5246632Z`，health 為 mongo=true／playback_e2e、interval 10、prompt `section-notes-v6`；[snapshot](../app-temp/data/validation/runs/2026-09-29-week3/application-results.json) 保存實際 health 及 executions。新 prompt 的 offline protocol／SDK schema／notes-redesign checks 和 .NET build 零 warning／error 通過。自然歷史 executions 使用自身保存的 v1–v4 及 chat-v1，沒有改寫成最新 version。

Validation server 現從 `app-temp/api/bin/week3-validation/net10.0/Playback.Api.dll` 啟動，確保既有音訊根目錄解析為 ignored `app-temp/data/audio`。較早隔離輸出位置產生的三個本次合成 WAV 已逐個精確搬至該路徑；全部 SHA-256、實際逐段 GET、混合播放及 system-only 播放核對通過。未搬動或刪除使用者檔案。[audio readback](../app-temp/data/validation/runs/2026-09-29-week3/audio-relocation-readback.json)。

保留 [production review UI](http://127.0.0.1:5177)（API 5081）及 offline preview 5174（API 5079）供本機查看，Mongo 及測試資料保留。自動 notes／terms／organize 與 review API 的 external ASR 仍關閉；原 API 5078 未更動。

## Provider 及 cost

批准前的socket／auto-review拒絕已保存在 [archive](../app-temp/data/validation/runs/2026-09-29-week3/archive-sandbox-failure/provider-results.json)。明確授權後才傳首一小時文字到 `api.typesafe.ai`／`aiplatform.googleapis.com`，沒有上傳音訊。初次provider runner使用205個二段文本imports及memory store；之後真實Mongo session使用137個三段imports，兩者均保存原409段的文字／時間／hash，不能混作相同source數量。

真實Jev完整內容allow=true（probability0.74）、碎句allow=false（0.09）；automatic run先wait再allow。Gemini v1三次因JSON／未知section id失敗；API v2/v3另三次因point正文／inputcoverage／citation contract失敗。修正成單一point正文schema、限制id、程式生成citation、partial缺失保持pending及捕捉section version後，有三版保存。實際 **note attempts=9／completed=3／failed=6**，沒有以成功版本數扣budget。一次organizer v4失敗保存usage；captured version修正後未追加付費重試。

`week3-report.mjs` 合併初次records/direct與Mongoactivity，以session／execution id去重；包含failed請求，validation error不冒充額外請求。現在22筆都有可用usage；標準text token估算 **USD0.049239448**，按兩筆provider確實回報的cached input另列 **USD0.047410198**。[cost-summary](../app-temp/data/validation/runs/2026-09-29-week3/cost-summary.json)／[cost-report](../app-temp/data/validation/runs/2026-09-29-week3/cost-report.md)。依已核對 [Vertex](https://cloud.google.com/vertex-ai/generative-ai/pricing)／[TypeSafe](https://docs.typesafe.ai/models) 的global standard text rates；不是帳單，地區差額、credits及unknown usage未計。

保存的public-web metadata另有3個Search queries。Gemini3共用每月5,000 queries included，超額USD14／1,000 queries；剩餘quota未核對，若這3個全部billable，另 **USD0.042**，token加此情境約USD0.08941–0.09124。這是保存query metadata的情境估價，不是帳戶billing；初次被拒絕detail的query metadata未知，沒有把它算作零費用。[定價](https://cloud.google.com/vertex-ai/generative-ai/pricing)

三版保存不代表全堂完成：42個imports有written points、65個deferred、30個pending；latest processedThrough不能當完整coverage。自然v3沒有table／diagram，部分command／名稱及示範狀態被過度確定化；私人Q&A亦有ASR技術名稱解釋錯誤。逐section來源、引用錯配、推導／例子／圖表及不確定性見 [自然品質人工核對](week3-natural-quality-review.zh-HK.md)。最新v6／chat-v2已明確要求保留不確定性和示範失敗；尚未取得修正後的自然品質證據。

CPU probability0.64、Microsoft Symbol Server0.37低於highlight門檻，沒有強行explain；這只驗證實際不選擇路徑。首次直接term detail因Google Search沒有verifiable source而拒絕保存，仍保存usage。新自然長詳解品質未通過。硬件／native realtime／自然ASR長錄音及whole-browser RSS本輪未驗證。

原九次note上限已用完。已提出追加最多三次筆記生成的問題，回覆前不增加請求；即使批准，仍要審閱重要deferred內容與organizer結果，不能保證三次就能通過品質驗收。所有DB、付費回應及失敗資料保留，沒有commit／push／清空資料庫。
