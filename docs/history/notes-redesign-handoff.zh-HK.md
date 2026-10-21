# 下一個 session：ASR 消化、10 秒 Jev 筆記決策、引用閱讀及 group flow

後續補完及使用者指定的一小時 frontend load／loading／cost／E2E 核對見 [Week 3 最新驗收](week3-one-hour-validation.zh-HK.md)。下方原始研究／初版狀態保留作歷史，不代表功能仍未實作。

這是原始研究及實作交接。2026-09-29 已完成下表所列程式與離線驗證；原本 2026-09-28 的只讀證據及 implementation prompt 保留在後面。**每 10 秒由 Jev 判斷是否呼叫筆記 LLM，取消固定等待 90 秒。** 10 秒是決策檢查週期，並非每 10 秒必須生成整份筆記，也不是保證模型在 10 秒內完成。

最新補充：使用者指出舊筆記內容消失，並偏好**更多有用圖表、更完整解釋、以及高亮詞能展開更多說明**；亦要求檢查持續 UI 編輯／逐字稿更新的記憶體問題，考慮 virtualized transcript 與定期合併。先處理內容保留，再改善呈現與效能。詳見 [筆記內容及記憶體核對](notes-retention-memory-audit.zh-HK.md)。

## 2026-09-29 實作及驗收狀態

完整檔案、測試、量測、限制及有限live驗證計畫見 [逐項驗收報告](notes-redesign-validation.zh-HK.md)。**Live API 未更新；5078 最後核對無法連線，MongoDB unavailable；恢復 preview 未套用。** 本輪未新增付費provider／音訊上傳、清空DB、讀寫.env或commit／push。

| 原問題 | 目前狀態 | 證據／未完成項 |
|---|---|---|
| 刷新 UI 是否需要重啟 app | UI可reload；新後端尚未在live生效 | 隔離5079 build／health／idle驗證，已停止；後端程式需正常重啟。沒有重啟錄音服務。 |
| 90秒太長，Jev每10秒決策 | 已實作及離線驗證；live未驗證 | NoteScheduler／NoteAgent／JevNoteGate；可控clock實測9.9秒零call、10秒allow→一次LLM、idle／wait／去重／retry／slow sessions／Stop／manual／競態。 |
| ASR數學、例子、論點需要完整消化 | section／coverage／prompt已實作；自然模型品質未驗證 | 未修改sections保留，模型patch／points／explicit deferred。原ASR抽樣核對95個parts，R/n是由平均分享整理的目標、R/10例子有原話；不是新模型品質證明。 |
| VAD section太碎，希望combine／compress | 穩定語意sections及跨chunk输入已實作；語意compression品質未驗證 | 原始ASR及source identity不改；完整pending＋context、延後半句、late final、deferred窗口前進離線驗證；Jev只判斷，不生成摘要。 |
| reference buttons過多／五分鐘範圍 | 已實作及UI驗證 | Reading／Sources、每section短入口、≤300秒display ranges、精確popup集合／間隙／播放、持久化cite IDs、unknown unavailable、零生成POST。實際新prompt成本未測。 |
| 指定section由另一個任務整理 | 已實作及離線行為驗證；自然內容未驗證 | section-organize-v1实际request、base／protected／其餘section保留；可選30分鐘有界策略，預設關。 |
| group … 查看flow／prompt／trigger／lifecycle | 已實作及UI驗證；DB executions未驗證 | 約70vw modal、配置graph與真實execution分開、共享prompt、gate／delay／usage、failure／retry／Escape／窄屏／零生成POST。live interim ASR calls尚無execution紀錄。 |
| 模型是否真用prompt、逐項證據 | request紀錄已實作；自然服從品質未驗證 | 真正call保存prompt版本／hash／text、input identity／bytes／sources、模型、base、usage及latency；offline SDK／transport驗證不能替代自然評分。 |
| 舊筆記內容不见／history 100版限制 | 保留修復及preview已實作；DB restore／套用未驗證 | v118＋v112五個候選只讀preview、意圖刪除不復活、user-edited sections保護、分頁及直接查版。快照只有v19–118；更早遺漏及全堂coverage未完成。 |
| 更多圖表、完整解釋及高亮詳解 | 呈現／保存／prompt已實作及UI驗證；生成品質未驗證 | term-detail-v2短／長重用、明確detail升級、ordinary詞排除、來源支持閱讀樣例四圖。沒有把手寫fixture當模型品質成功。 |
| 持續更新／編輯記憶體 | 已修復已重現的累積／retention／allocation 路徑；10分鐘 fixture通過；三小時未驗證 | [專項證據](frontend-memory-validation.zh-HK.md)：30次partial diagram殘留由30變0；Activity DOM 9,620→1,766；真實scroll parent／wheel／uncovered tail已修正。2,700＋292 finals／440cycles，定期GC約15–16.4MiB；切小session11.93MiB、215DOM、舊source IDs 0。未完成live browser／三小時wall-clock、server逐record增量DB查詢或新定期展示合併。 |

## 2026-09-28 原始只讀核對及問題清單

2026-09-28 的本機只讀核對；資料會隨錄音及後續改動變化。未呼叫新的外部 provider，未重啟正在錄音的服務，未更改 DB 或應用程式行為。

| 使用者提出的問題 | 已核對的證據 | 尚需實作／驗收 |
|---|---|---|
| 刷新 UI 是否需要重啟 app | 5078 health：`startedAt=2026-09-28T12:49:08Z`（香港 20:49）、build `75291824-f448-4f2c-a678-6b92bf73a322`、`chatConversations=true`、`geminiModel=gemini-3.1-flash-lite`。API DLL 寫入時間 12:49:06Z；錄音狀態為 recording。 | 目前已有本輪新版能力，無需為刷新 UI 再重啟。日後 C#／後端 prompt／環境配置變更需重新啟動 API；先在 UI 正常停止錄音，再於原 dev 終端 Ctrl+C、執行 `pnpm.cmd dev`、重載網頁。重啟本身不會實作新功能。 |
| 90 秒太長，應讓 Jev 每 10 秒決定要不要記筆記 | `NoteAgent` 每 8 秒 scan，但 `AutomaticInterval=90s`；首次亦延遲 90 秒。Jev 未參與這條筆記觸發路徑。checks 還明確斷言 90 秒。 | 新增真正的 Jev note gate，移除首次及後續 90 秒等待，測試有新內容時 10 秒決策和允許後即時排入 LLM。 |
| ASR 未被消化成易懂筆記：數學、例子、論點標籤，需要反覆來回讀 | prompt 已要求 takeaways／bullets／table／diagram，但 validation 主要核對引用、語言和 note version。Week 3 v115 快照已有要點和一個 Mermaid block。 | 不能再沿用「完全沒有圖」的舊結論；也不能以有 bullets／圖便判定數學解釋、例子與論證已達標。需逐段對照來源檢查內容保留、解釋及重組品質。 |
| ASR section 太碎，VAD chunk 不可信，希望按意思 combine／compress | `SourceReferences` 同 source 依時間拼接，最多 60 秒／16 chunks，gap 1.5 秒或 overlap 超過 1 秒會切組；`GroupText` 只是 join。 | 分開原始錄音 chunk、語意 section 和 UI 引用範圍；跨 VAD 邊界整理完整意思，保留原始 ASR。 |
| reference button 太多，想隱藏或合成約五分鐘範圍 | 已有 paragraph 層級短引用／popup，但 NotesPanel 只有 Preview／Edit／Live draft，沒有 hide references 模式。v115 保存 Markdown 27,151 字元，其中已知來源 ID 標記 24,123 字元（88.8%）；這是儲存格式比例，**不是 UI 畫面佔用比例**。 | 閱讀／來源模式；一組時間範圍展開精確來源。另檢查長 ID 展開是否膨脹 prompt、耗盡 output token budget；用實際 payload／completion 資料驗證，不能由儲存比例直接推論費用。 |
| 定期由另一個 agent 整理指定 section，太長時改 point form／chart | 目前 NoteAgent 仍回傳整份 Markdown；沒有獨立的 section 整理 agent。 | 有界的 section 整理工作、明確 prompt、可還原結果，保留使用者編輯與來源。 |
| 在 nav group 的 … 查看 agent／prompt／trigger／graph／lifecycle，約 70% modal | `SessionNav` 的 group menu 只有 Rename／Delete；Activity 是 session 級執行記錄，沒有 group flow modal。 | group menu 加入口；顯示 group 內真實流程與執行、prompt 版本／生效內容、關係、觸發條件和生命週期。 |
| 懷疑模型不使用 prompt，之前測試沒有逐項核對 | `notes-chat-check.mjs` 使用手寫 Markdown fixture，檔案亦註明只驗證 presentation；不能證明自然課堂模型遵守 prompt。 | 對真實呼叫保存可核對的 prompt 版本／hash／模型／輸入來源及結果；離線 UI 與生成品質分開報告。所有需求逐項回報證據／失敗／未測。 |
| 舊筆記內容不見了，目前不完整 | Week 3 v112 有公平分配、速度測試、安全及五層／封裝；v113 在 20:50:51 保存後，正文改為近期話題，以上內容缺失，至 v118 未補回。歷史仍有 v112。來源 metadata 卻持續累積並保持 completed。 | 修復全篇 replacement／來源完成狀態與真正內容 coverage 的落差，安全整合舊與新內容；不能只 restore v112 丟掉後續內容。 |
| 更多 diagram、解釋及高亮詞詳解 | 筆記 prompt 要求每題 2–4 bullets、每 bullet 約 30 words；grounded term 限一個定義＋最多兩點／一例、90 words／768 tokens。 | 用 section 級資源限制保留全堂內容；對數學、例子及關係給足解釋。hover 短版＋展開完整解釋，保存並重用，減少無意義高亮詞。 |
| 持續更新／編輯 UI 時記憶體增加，希望虛擬化及合併逐字稿 | TranscriptContent 將全堂各列 map 入 DOM；全 session 約 1.74 MB，每 4 秒重新取回，capture 每 250 ms、Activity 每 700 ms。隔離快照有 738 mounted rows／17,210 elements；量測結果見核對文件。 | virtualized visible rows、增量資料／穩定 identity、渲染及索引重用；以回收後 heap、DOM、CPU 和 scroll／跳轉測試驗收，區分 allocation churn 與 retention leak。 |

本輪沒有完整 browser 視覺 QA、自然課堂人工評分或 Jev note-gate provider 驗證。health 中 `jev=true` 只表示配置存在，不能證明新 gate 的能力或品質。

## 可直接交給另一個 session 的 implementation prompt

請在 `C:\Users\peter\Documents\Github\playback` 實作以下功能。先讀 `AGENTS.md`、`docs/business-requirements.md`、`docs/scope-decision.zh-HK.md`、`app-temp/README.zh-HK.md`、`app-temp/STRUCTURE.md`、`app-temp/docs/notes-chat-update.zh-HK.md` 及本文件。核對當前 Git、API build、錄音狀態和實際 code；保留 WIP。舊 handoff／README 的 90 秒描述是現況，不是此任務的目標。

遵守 AGENTS.md 的資料及操作限制。不要讀／改／輸出 .env，不要自行 commit／push／刪使用者檔案。清空或破壞性重整 DB 前，列出目標、損失及重建計畫並取得明確批准。預設離線；本交接不是新增付費 live test／音訊上傳授權。核對已有授權是否適用，再執行有限 live 驗證；沒有授權時先完成離線部分並列出精確測試計畫。變動的 provider／.NET／Agent Framework guidance 查官方文件。

### 0. 優先修復內容保留與補回歷史內容

- 先從保存的歷史、ASR 和教材建立主題／內容 coverage；核對 v112→v113 的刪除，亦檢查更早更新是否已遺漏。v103–v110 的 Markdown hash 完全相同，來源 metadata 卻由 533 累積到 611；不能把「所有 source IDs 已登記」當成已實際消化。
- 程式目前每次將模型的整份結果保存為下一版，只驗引用／語言／版本。已有內容雖在 base note 送進模型，但缺乏防止整個舊主題被丟掉的驗收；manual revision 無 pending 時只重送最後 40 段，也不會完整重建已掉失的早段內容。修復不能只增加一行「保留內容」prompt。
- 在 section 級更新上建立可觀察的來源／重點 coverage，以及保留未修改 section 的機制。允許有理由的合併／精简／使用者刪除；不能用純字數或 heading 文本比較阻止合理整理，也不能自動復活使用者刻意刪除的內容。
- 提供可審閱的恢復 preview，合併歷史遺漏的來源支持內容與最新 sections；保留 user edits、現有後段及完整版本。不直接覆寫 live notes；重啟／新生成不能再次把修好的早段丟掉。可透過新的版本保存並還原。
- History 的 GET／restore 現只搜尋最近 100 版；提供分頁及按版本直接定位，確保舊版仍可讀／還原。保存完整 history 不代表 UI／REST 已能找到全部。

### 1. 每 10 秒讓 Jev 決定是否呼叫筆記模型

- 僅有新 confirmed ASR／相關輸入變化的 session 需要決策；idle 無新內容不呼叫 provider。以完整未消化內容及必要的既有 section context 作有界輸入，避免只看最後一個 VAD chunk。
- 真正使用 Jev 的結構化問題判斷現在是否值得更新筆記，例如新重點、完整例子／推導、需要修正的說法或換題。先核對官方 schema／可用模型，設計並驗證 note 專用問題；**不能拿現有「術語值得解釋」的 probability 當 note-update probability，也不能只把 timer 90 改為 10 便稱完成。**
- Jev 允許後立即排入 note LLM，不再額外等待 90 秒；wait 時保留 pending ASR 累積，下次有新輸入再評估。同一未變 input 不重付費評估。不要將 wait 標成已完成筆記。
- 排程每 10 秒檢查，provider 工作採有界並行及每 session 去重，慢的 Jev／LLM 不應阻塞所有 session 的下一輪。檢查現有術語 rank 的全域 semaphore／120 秒 timeout，避免新 gate 被詞語排隊長期拖住。共享適當 provider transport／discovery，避免複製協議實作；任務決策仍須各自清晰。
- 確保一直 wait 不會永久遺漏有意義內容；以可測的累積量／完整性／收尾策略處理，正常決策路徑仍為 Jev。Stop 時再評估待處理內容，manual Revise 保留立即執行入口。
- 明確區分 schedule delay、Jev latency、LLM queue／generation latency；錯誤、timeout、未配置及重試顯示真實状态，不能假造允許／成功。記錄輸入身份、結果、模型、prompt 版本、耗時及 provider 有回報的 usage。
- 移除 `NoteAgent`／checks 的 90 秒要求，更新相關文件。防止並行、重啟、language／note edit 變化造成重做或舊結果覆寫。

### 2. 按意思消化 ASR，並產生易懂內容

- 原始 audio／ASR 保留；VAD 是傳輸及收音邊界，不能當章節語意邊界。同 source 的碎句可跨 chunk 合併，不能錯拼 mic／system 或不同 session。
- 新增有穩定身份的語意 section，支援延後完成半句、修訂早先意思、主題延續與轉換。時間／長度限制作資源保護，不能取代语意判斷。Jev 決定是否值得呼叫 LLM／更新，生成模型負責消化及 compression；沒有證據前不能聲稱 Jev 會生成摘要。
- 筆記應減少讀者在 ASR 與筆記之間來回：以來源支持的核心概念、定義、數學式及符號意思、推導步驟、標明的例子、論點／理由／結論組織內容。只在存在時使用，不能為填模板捏造數學、例子或關係。
- 最新偏好是圖表更多且解釋更完整，不能把本輪「2–4 bullets／每點 30 words」當不可變產品限制。按內容使用層次圖、比較表、流程／關係圖；數學應說明符號、條件與例子。例如本 session 的公平分享目標可保留 `R/n`、`n=10 → R/10` 及同一 bottleneck 的關係，而不是把它整段丟掉。安全／五層與 encapsulation 等早段亦需整理成讀者能理解的內容，逐項驗證原話。
- 修正口語重複／斷句，保留重要條件、反例及不確定性。區分「講者已說明」與有來源的 AI／網絡補充；沒有建立的定義不得當課堂事實。
- 更新受影響 section，避免每 10 秒重寫整份長筆記。選擇一個一致的資料／版本模型，保留使用者編輯、完整歷史及可還原操作；處理 late ASR 和有界 input／output。

### 3. 引用閱讀模式與時間範圍

- 提供易讀的 Reading／Sources view（或等效 toggle）；Reading 收起 citation buttons，Sources 展示可展開來源。切換只改呈現，不刪保存證據或觸發模型。
- 同一 section 的相關引用可合成一個短來源入口，顯示真實 session 時間範圍，如 `10:00–15:00`。若是五分鐘，這個例子才是五分鐘；`10:00–10:05` 按 mm:ss 是五秒，需明確標示時間格式。
- 約五分鐘是顯示聚合上限／方向，不是強制每五分鐘 ASR 摘要，也不能把區間內所有 chunk 都冒充某一個論點的證據。展開保留實際引用集合、間隙、來源、原文／音訊跳轉。
- 處理 pause／resume、多次錄音、source overlap、早段 ASR 晚到及新增來源，引用 identity 不因重新編號而錯連。保持未知來源的真實不可用狀態。
- 評估以可持久化 section／citation identity 保持短引用的方法，避免保存及下一次 prompt 反覆攜帶巨量 chunk IDs；完整 provenance 另可精確還原。不能只用 prompt-local alias 當永久 identity。
- 高亮詞提供可讀短解釋與展開詳解：定義、課堂用途、來源支持的例子／數學、相關概念及限制，適合時附有來源的圖表。擴充保存內容／prompt 版本及資源限制；重用結果、hover 只讀，不能每次 render 重新呼叫 provider。課堂說明與外部補充仍區分清楚。檢查不該被當學術術語的普通詞（如目前結果的 `details`），避免高亮更多卻幫助更少。

### 4. 定期整理指定 section 的 agent

- 加入可選指定 section 的整理功能及有界自動策略：當內容太長、重複或結構不清，再整理為較好的 point form／table／chart。這是與快筆記生成分開的任務，不要所有來源事件都新增一次全篇整理。
- 使用獨立、可核對且真正在請求中生效的 prompt。先理解來源／section，再決定結構；圖表必須增加理解而非裝飾。保留推導、例子、論證及證據，不能以縮短字數犧牲正確性。
- 使用同一份 notes／section 及執行紀錄，避免多個 agent 各維護一份不同 truth。合併時檢查 note／section base version，保留未保存 editor／caret／scroll，衝突需可見且可恢復。

### 5. Group 的流程與生命週期 modal

- nav group 的 `…` 加 View AI flow；桌面約 viewport 70% 的 modal，窄屏適配，含 keyboard focus、Escape、關閉、loading／failed／empty states。
- 顯示該 group 所屬 session 的真實 pipeline／agents，包括 ASR、語意組合、Jev note gate、筆記、section 整理、術語／explanation、translation、chat 的觸發和關係；標出 disabled／未配置／未實作能力。group 是查看範圍，不代表允許跨 session 混用來源。
- 各 agent 可查看 provider／model、生效 prompt／版本、輸入／輸出角色、trigger、skip／cache／retry／cancel 條件及依賴圖。區分配置流程與真實近期 executions；沿用及擴展 `AiActivity`，不要補造歷史呼叫。
- 可從圖／列表看 queued／running／completed／failed／cache-hit 等 lifecycle、時間、延遲、涉及 section、base version 和安全錯誤；切換 group 防 late result 污染。顯示 prompt 模板及必要的脫敏輸入資訊，不顯示 credentials 或 provider 私有推理。
- 閱讀 modal 不發生成／排序請求。流程描述和實際 runtime 設定使用一致来源，避免文件式假圖與真實程式不符。

### 5a. 長逐字稿／反覆編輯的記憶體及 CPU

- 先重現使用者流程，記錄 idle、live interim／final、notes 編輯／preview／diagram、高亮詳解、來源 popup、跳播及 session 切換的 CPU、payload、DOM、JS heap。量 GC 前／後，檢查 detached DOM／listeners 的 retention；截圖的瞬時 MB/s 不能直接命名為 leak。
- 用真正的 list virtualization 控制 mounted rows，含可變高度、展開原文／翻譯、日／小時、pending interim、scroll anchor 和 overscan；引用可定位未掛載來源、scroll-to-item 後展開和 highlight。搜尋／選取／播放 identity 不依 DOM 全堂存在。
- 可以定期合併展示已完成的連續逐字稿／語意 section，但只改表示／索引，不刪原始 ASR 或改 source identity。關閉 details／CSS 隱藏不是 virtualization；合併文字也不等於記憶體問題已解決。
- `handlers.ts` 每 4 秒取得整個 session 並全部 `setSession`，錄音 finalized 亦重取；`refreshSessionSnapshot` 沒有全域單一 in-flight 控制。採用有界增量查詢／更新、重用未變物件、去重／取消舊請求，避免慢回應堆积。不要只把 poll 放慢來掩蓋成本或犧牲 live 行為。
- 索引重用 term-to-source／timeline／references；把 recorder level／clock／player／Activity 的更新與整堂 transcript／Markdown 重算分開，避免每次 capture／editor 改動都重建全列及 Markdown AST。處理 Mermaid render 的去重／取消與 popup／timer／listener cleanup；不要引入無上限的 cache。
- 保持至少三小時、mic＋system source 的有界 UI 成本；測 1,350／2,700 原始 chunks、連續編輯、合併前後、回到較小 session、125% zoom 和窄屏。API payload 限制與 UI virtualization 各自驗證。用修復前後相同 fixture／情境比對，列出回收後 memory 及 DOM 是否回落。

### 6. 驗收：逐項檢查，不把 prompt 改動或 UI fixture 當品質成功

- **10 秒 gate**：用可控制 clock／provider responses 的離線行為測試，覆蓋 new input→Jev allow→一次 LLM、wait 保留累積、無新內容零請求、相同 input 去重、不同 session 互不阻塞、slow／error／timeout、重啟恢復、manual／Stop、語言／使用者 edits 競態；實際 interval 行為不只斷言常數。
- **合併與壓縮**：跨 VAD 的半句／公式、例子 continuation、換題、長 pause、兩個 source、late final／重試。compression 後每項重點能對回原話，原始 ASR 可讀可播，無遺失推導條件。
- **品質**：repo Week 3 `transcript.txt` 是全堂，`sampleAudio.m4a` 只有首 20 分鐘。選已對齊且包含需要驗收內容的片段；文本評估不一定需要重新上傳音訊。列出來源、生成前後、模型及 prompt 版本，人工核對數學／例子／論證／精簡度／幻覺／不確定性。合成 fixture 可以補充 edge cases，不能代替自然內容評分。
- **引用及 UI**：Reading／Sources 切換、每 section 引用密度、約五分鐘範圍的正確集合、早晚來源可跳播、無來源錯綁；不同視窗及 zoom 目視截圖。確認不用反覆開一堆 buttons 才看懂內容。
- **整理 agent／flow**：指定 section 整理，其餘 section／使用者 edits 保留，結果可還原；group … modal 顯示與真實請求一致的 prompt／trigger／graph／lifecycle，切換及錯誤可見，讀取不新增付費請求。
- **內容保留／解釋**：加入新章節後，早段公平分配公式／例子、安全、五層／封裝仍可讀且有來源；重組後亦保留，使用者刻意刪除則不復活。恢復 preview 合併新舊且可還原；高亮詳解比現有 90-word 版有實質補充，圖表關係有來源。所有歷史版本可分頁／直接查閱。
- **效能**：長 transcript 僅掛載可見區／overscan，後續切換小 session 能釋放舊列表。確認引用跳轉、展開、播放及 selection 仍正確；持續編輯／interim／合併後回收 memory 不持續隨迭代上升，附 browser／build／資料量／耗時和 artifacts。
- build／相關離線 checks 使用隔離輸出及端口，不覆蓋正在運行的 API。live 呼叫按適用的明確授權有限執行；保存 latency／response／usage／失敗，日後展示用保存結果，不反覆重付費。
- 最終交付更新本文件問題清單：每項標為已實作及已驗證／已實作但未驗證／未實作或失敗，附具體檔案、測試及結果。明示 runtime 是否已更新。不能只交一個籠統「全部完成」。

## 優先查閱的實作位置

`app-temp/api/Services/Ai/Agents/NoteAgent.cs`、`Agents/TermReviewAgent.cs`、`Providers/JevTermClassifier.cs`、`SourceReferences.cs`、`AiActivity.cs`、`Db/Models.cs`、`Db/PlaybackStore.cs`、`Endpoints/HealthEndpoints.cs`；web 的 `pages/NotesPanel.tsx`、`pages/SessionNav.tsx`、`Component/Markdown.tsx`、`Component/SourceCitation.tsx`、`pages/ActivityContent.tsx`、`types/api.ts`；checks 的 `Program.cs` 及 `web/src/test/notes-chat-check.mjs`。先檢查實際 caller／schema／ownership 再決定重構，不為每項要求另加互相重疊的 agent 或 state。
