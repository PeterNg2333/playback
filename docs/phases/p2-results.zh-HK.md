# P2：長課堂、合成廣東話與測試費用

2026-10-20。使用者批准 Qwen／Gemini Week 3 與合成廣東話 replay，共同上限 US$3。Gemini guard 停止後，使用者質疑費用；**不提高上限，不再啟動新付費測試**。原在跑嘅 Qwen 亦已取消以停止支出。完整雙模型比較未完成。

## 測試範圍

Week 3 使用 repo `app-temp/data/test-audio/sampleAudio/transcript.txt`，110,763 chars、164:35、633 個分組來源。呢份原文未超過 200,000-char lecture window，**冇驗到 lecture window 滑動／prefix eviction**。係文字 replay、production NoteWorkflow、in-memory store；無 ASR／Mongo／新音訊上傳。Prompt `tree-notes-v15`；之後 P1 v16 修正不在呢批 benchmark 內。

| Run folder（`app-temp/data/validation/runs/`） | 已觀察結果 | 費用 US$ |
| --- | --- | --- |
| `2026-10-20-wave1-p2-week3-gemini` | 約 109 分鐘 guard 停；191/197 writer 成功、31 organizer；399/402 已到達來源被引用，3 pending；未到全部 633 來源 | 1.9491701，list-price estimate，非帳單 |
| `2026-10-20-wave1-p2-week3-qwen` | 取消前最後 progress：133 分鐘／227 writer calls；之後另有 usage。未保存 in-memory note／完整 per-call evidence，最終覆蓋未知；見 interrupted-run.json | 最後 observed accounted 0.629979；非最終帳單 |
| `2026-10-20-wave1-p2-cantonese`，Qwen | 完整 8 分鐘，8/8 writer、8/8 引用 | 0.00536041，provider reported |
| 同上，Gemini | 8/9 writer、8/8 引用；一次空內容失敗後由之後批次補回 | 0.02142959，estimate |

Gemini 長測：5,825,642 input tokens、0 cached、80,591 output；writer median/max 2,638/17,370 ms；127 topics（7 empty）、281 points、0 placeholder heuristic。失敗包括未處理來源、output truncation，以及本地 budget refusal；最後一項唔係 provider HTTP failure。

本次 P2 四項最後可見費用合計約 **US$2.60594**（Gemini estimate + OpenRouter reported/accounted，非帳單；取消時仍在途請求嘅實際計費可能有差異）。不包括 P1 錄音／ASR 或 P4b。Qwen cancellation 暴露 runner 冇中途 checkpoint 嘅限制；冇為咗取得完整 note 而繼續付費。

按 call 次序四等份嘅 input tokens 分別係 687,695／1,421,478／1,648,615／2,067,854，cached 全部 0。Organizer 31 calls 嘅 summaries 合計丟棄 66 個過深 moves、11 個範圍外 operations；`completed` 包含冇變更嘅結果，唔等於成功重組。

## 點解測試會花到呢個費用

每批寫作會再送累積講課內容及現有筆記，另有 organizer request。109 分鐘已累積約 583 萬 input tokens；Vertex 呢次冇 cache 折扣，令總費用升到約 US$1.95。呢個係特定長測 cadence 嘅累積費用，**唔係單一普通 request 價格，亦未建立可靠每堂成本預測**。不應為比較而要求使用者加錢；下一輪先用保存 replies／fixture 做 regression，小段固定範圍驗證；任何新增 live scope 另有明確授權先做。

Runner 新增 `--source`、`--minutes`、`--script zh-Hant`、`--max-usd`；候選模型共享同一個 command budget。Admission 用保守 request token bound 預留費用，failed request 缺 usage 時預留 bound，因此 guard 可早於實際支出達 cap 停止。費率係 2026-10-20 snapshot，未來 live 前需重新查核。預設仍係首 20 分鐘、raw script、offline fixture。

已有 live 結果預設直接讀，核對檔案 SHA256、span、script、model list 同 recovery source，設定唔符就拒絕，唔會默默再付費。`--resume-from` 係載入保存 note／activity 後手動 Generate 未引用來源，**唔恢復 scheduler checkpoint，唔算 uninterrupted replay**。P4b 用咗呢個方式；P2 冇為 Gemini 補跑。

## 廣東話：清楚標示合成

`checks/Fixtures/cantonese-lecture.txt` 係人工寫嘅八分鐘廣東話／英文文字，唔係真人 ASR。包含 paging、TLB、120 ns 計算、FIFO/LRU/Clock、`NRU (?)`、working set／thrashing、`示范`。兩個模型數學例子均得 120 ns；Qwen 10 topics/16 points，Gemini 10 topics/12 points；兩者未觸發 organizer。

Qwen input/cached/output 37,363/15,360/3,860，writer median/max 9,869/19,276 ms。Gemini 53,234/3,993/2,615，2,194/5,990 ms。

分開保留 `replies.json`（原始 model JSON）同 `notes.md`／`note.json`（OpenCC 後）。離線 paired check 確認 raw `示范` 保留，saved note 變 `示範`；兩份 live saved note common Simplified-only heuristic 均為 0。呢個唔係所有異體字、用詞或語義正確嘅保證。Qwen 傾向書面化；目前 `zh-Hant` setting 本身要求書面中文，不能直接判為違反語言設定。Gemini 較保留口語。Qwen 有加入原 fixture 未支持嘅 SRAM/DRAM cache 解釋，以及重複放置位址例子；有引用仍可能加咗新知識。

對全部 raw replies 嘅 JSON string values 做相同 Simplified-only 字元 heuristic：Gemini 長測 0、廣東話 Qwen 0／Gemini 0；對應 final saved note 均 0。Raw 係所有 requests 嘅累積輸出，saved 係最終文件，兩者唔係同一內容單位；共用字詞「示范」本身亦唔喺此 heuristic 字表內，要靠 paired OpenCC check。

## 人工對照十段（Gemini 長測）

由保存 note 均勻抽取，逐段看所引用原文，唔用 cited/total 當準確度。

| 抽查內容 | 對照結果 |
| --- | --- |
| AI training certificate | 主旨有原文支持 |
| EAX 與 32-bit 命名 | 有支持 |
| 兩個 SSDT tables | 語境相符，但原文 `SST(?)` 嘅不確定性被正規化，需保留疑問 |
| 找 kernel address | 引用語境支持 |
| l4 listing entries | 內容相符，directory(?) 保留疑問 |
| Win32 objects(?)／registry | 語境相符，不確定標記保留 |
| Idle process PID 0 | 有支持 |
| Parent/child 三種 resource sharing | 有支持 |
| Command prompt title 與 grandparent demo | 有支持，標題較籠統 |
| SelectMyParent、Notepad PID 66 | 操作有支持；「advanced threat analysis」用途係新增推論，引用未支持 |

結論：快慢／引用覆蓋已有基準，語義仍有擴寫風險；organizer 嘅範圍／深度丟棄需要另按保存 activity 分析，不應用更大付費 replay 取代固定 regression。

## 驗證與未完成

Build 及完整 offline suite 通過，包括真實 fixture workflow raw/OpenCC 對照、raw reply 不變、來源 provenance、duration bounds。正常結束嘅 run 會保存 `sources.json`、逐 call `calls.json`、usage/cache、latency、raw replies 同 note；取消嘅 Qwen 只有終端摘錄，不能當完整 benchmark。

未完成：Gemini 全 164:35、200k window 滑動、三小時硬件／ASR穩定性。即使最後 Qwen 引用齊全，亦唔代表語義／帳單／真實錄音已驗收。

範圍外改動：`FixtureNoteModel` 新增 placement fixture reply、`NoteReplayChecks` 同 Program 登記，用 production workflow 驗證 OpenCC；冇加入新套件，本輪一併提交至 main，未 push。
