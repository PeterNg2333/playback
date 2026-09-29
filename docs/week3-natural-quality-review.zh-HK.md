# Week 3 首一小時：自然筆記及問答人工核對

2026-09-29，核對 repo `sampleAudio/transcript.txt` 的首一小時文本與保存的自然結果。輸入 SHA-256：`21f256c8b1b93cc5558ea2cca601efd76f159091705b581c87780bbd50c2628f`。不是音訊準確度評分；没有重新上傳音訊。

**整體未通過。** 傳輸、引用身份、版本保存及舊 point 保留可驗證，不能證明每項概念、數值、命令和推導都正確。原九次筆記生成已用完；六次被 contract／coverage guard 拒絕，三版保存成功。Mongo v3 有5個 sections、13個 points、3,845字元正文，沒有 table／Mermaid。137個文本 imports 中42個 completed、65個 deferred、30個 pending。Deferred 是未完成來源，不應和 completed 相加作「全堂覆蓋」。

原始 [v1](../app-temp/data/validation/runs/2026-09-29-week3/db-note-v1.json)、[v2](../app-temp/data/validation/runs/2026-09-29-week3/db-note-v2.json)、[v3](../app-temp/data/validation/runs/2026-09-29-week3/db-note-v3.json)、[全文及 execution snapshot](../app-temp/data/validation/runs/2026-09-29-week3/application-results.json)、[逐段原文與 import 身份](../app-temp/data/validation/runs/2026-09-29-week3/db-input-provenance.json) 保留。以下 `_text_N` 皆指 session `7c70eb07fb1348e38dbbd5da0c5b22f7` 的實際 source，point／section 身份在 v3 可直讀。

| 主題／要求 | 對照來源與結果 | 判斷 |
|---|---|---|
| AI 學習方法、比較及論證 | `_text_0`、`1`、`5` 支持輸入課堂問題、比較講義與 AI、判斷 hallucination。第二 point 的「查閱論文或習題」卻引用 `6`、`9`、`10`；相關文字實際在 `7` | 主要意思可讀；逐 clause 的引用仍有錯配 |
| User／kernel mode、VM 練習 | `7`、`8` 支持 system call 與模式轉換，`13` 鼓勵 VM 實作；但 `13` 表示不會展示，而正文寫「承諾示範」 | 未保留示範狀態與不確定性 |
| Nt／Zw、核心函數 | `34`–`37` 支持相同位址／assembly、介面及表格指向實作。原 ASR 的介面／binary 名稱破碎，正文卻直接使用確定名稱 | 高層關係有來源；精確名稱需標假設或另查來源 |
| Debugger 操作 | `26`–`32` 支持 VM 網絡、symbol server、放大畫面及 reload。正文可讀，但未區分原 ASR 命令拼法和補充知識 | 部分通過；不可當可直接執行的準確命令教材 |
| SSDT／指令與數字 | `54`、`56`–`58`、`77` 提及 table、CPU entry、EAX 及破碎數字。正文給出確定指令／常數；沒有逐一解釋來源不清之處，也沒有有用表圖 | 完整解釋／不確定性／圖表未通過 |
| 結構與 debugger commands | `79`、`81` 支持 display type 與 EPROCESS；`83` 只是破碎的 wildcard／descriptor table 口述，正文變成 `!descriptor table` 可用命令；`91`、`94`、`95` 支持 pointer size、1D0、版本影響 | 不能由引用存在判定命令正確；應標 ASR unclear |
| 位移／offset／函數追蹤 | `108`、`109`、`126`、`131` 提及四 bit 位移；`115`、`124`–`126`、`128`、`131` 的數字及操作有破碎口述。正文只有概述，缺少可由來源確認的符號／條件／完整推導 | 未達數學與操作解釋驗收；不能補造完整 recipe |
| 內容保留 | v1、v2 所有已寫 point 的原文在後版仍可找到；兩個 section 出現 Earlier source-backed points，帶來重複文字 | 原文保留通過；重複／壓縮品質仍待自然 organizer |
| 私人 Ask Playback | 真實 streaming、citation validation、conversation 保存及 zero-call reload 通過；答案把破碎 `n k l n...` 解釋為「anti OS kernel」 | API/UI 行為通過；自然技術名稱解釋未通過 |

Public-web 問答取得7個來源，將 lecture insufficient 與 web answer 分開。來源按鈕、保存／重讀及375px畫面已用原回應驗證，沒有重付費。答案的 `.reload` lazy loading、`/f` 和 symbol path/cache 主要敘述，已對照 [Microsoft .reload 文件](https://learn.microsoft.com/en-us/windows-hardware/drivers/debuggercmds/-reload--reload-module-) 及 [public symbol server 文件](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/microsoft-public-symbols)。這是公開文件補充，不能回填成講師說過的內容；沒有逐一評分所有返回來源或 authentication 的細節。[原 browser run](../app-temp/data/validation/runs/2026-09-29-week3/browser-web.json) 保存了測試 injection 在 sandbox iframe 報錯的失敗；修正只在 top frame 初始化後，[保存回應重播](../app-temp/data/validation/runs/2026-09-29-week3/browser-web-read.json) 通過。

CPU 的 Jev explanation probability 為0.64，Microsoft Symbol Server 為0.37，均低於0.75門檻，所以沒有強行高亮或呼叫詳解。這驗證真實不選擇路徑，沒有驗證新的自然長詳解品質。初次 AI term 的直接 detail probe 有付費 usage，但因搜尋沒有可驗證來源而未保存成功。

目前 `section-notes-v6`／`section-organize-v6`／`chat-v2` 已明確要求：不由破碎字母補造命令、binary、abbreviation 或常數；標示 ASR unclear／假設；保留示範成功／失敗／只提出操作的區別；區分 entry-size multiplication、bit shift 和 offset。Build、SDK structured contract、來源／retention、clock、race checks 通過。這些 prompt 改動尚未取得新的自然品質證據；原自然結果仍保留自己的生效版本。

餘下驗收需要處理30個 pending、審閱65個 deferred 是否仍有重要內容、取得修正後的自然 writer／organizer 結果，再核對推導、例子、表圖及精確引用。追加最多三次筆記生成的授權已詢問，未收到回覆前不追加。不能保證三次便足以達到品質要求。
