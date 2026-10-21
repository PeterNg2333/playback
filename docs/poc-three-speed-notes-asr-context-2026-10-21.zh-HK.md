# POC：三速筆記同 ASR 長窗口 context（2026-10-21）

三個問題：(1) 唔再每 30 秒叫模型決定成份筆記嘅結構，改成「每分鐘 live jot → 每五分鐘寫一段 → 完場整理目錄」，筆記會唔會更好用、更平？(2) 將 ASR 改成較長窗口再加課程 glossary，術語會唔會認得準啲？(3) 逐字稿改咗或者要重寫筆記時，graph（lineage＋concept graph）可唔可以只更新受影響嘅部分？

全部用 repo 嘅 Week 3 sample（`app-temp/data/test-audio/sampleAudio/`）。Docker／MongoDB 冇開，冇用 `app-temp/data/audio` 入面嘅真實錄音。結果唔係產品行為；app 本身冇改。

**決定（2026-10-21，使用者）**：筆記方向用三速筆記＋graph 更新（lineage 決定更新範圍，concept graph 用喺 regenerate／Ask），筆記模型用 Qwen3.8 Flash。實作待做，見 [下一個 session 清單](next-session-checklist.zh-HK.md)。

## 做法

| 檔案 | 做乜 |
| --- | --- |
| [`poc-lib.mjs`](../app-temp/checks/Validation/poc-lib.mjs) | Provider 呼叫（Vertex Express、OpenRouter chat／STT）、固定 endpoint、無 redirect、timeout、回應大小上限、預算上限；每個成功回覆按 request hash 存喺 run folder 嘅 `cache/`，重跑唔再收費 |
| [`poc-notes-core.mjs`](../app-temp/checks/Validation/poc-notes-core.mjs) | 三速筆記嘅寫作部分：segment、outline、polish、concept graph、render（prompt 文字係 cache key 嘅一部分） |
| [`poc-three-speed-notes.mjs`](../app-temp/checks/Validation/poc-three-speed-notes.mjs) | 三速筆記全堂，同 baseline 比較。`--model` 用 app 嘅 `PLAYBACK_NOTE_MODEL` 寫法 |
| [`poc-asr-context.mjs`](../app-temp/checks/Validation/poc-asr-context.mjs) | 三段 90 秒音訊，Qwen3-ASR／Whisper 8 秒 vs 90 秒 vs 90 秒＋glossary prompt，Gemini audio 只作參考 |
| [`poc-note-update.mjs`](../app-temp/checks/Validation/poc-note-update.mjs) | 由保存咗嘅三速筆記開始：ASR rerun 之後 graph 更新 vs 完整重建，同埋有／冇 concept graph 嘅 topic regenerate（要先跑過前兩個） |

```powershell
# 預設 offline：只讀保存咗嘅回覆，缺少就停，唔會連網
node --env-file-if-exists=.env app-temp/checks/Validation/poc-three-speed-notes.mjs --model openrouter:qwen/qwen3.8-flash@none
node --env-file-if-exists=.env app-temp/checks/Validation/poc-asr-context.mjs
node --env-file-if-exists=.env app-temp/checks/Validation/poc-note-update.mjs
# 付費重跑要明確 --live 同預算
node --env-file-if-exists=.env app-temp/checks/Validation/poc-three-speed-notes.mjs --model openrouter:qwen/qwen3.8-flash@none --live --max-usd 0.2
node --env-file-if-exists=.env app-temp/checks/Validation/poc-asr-context.mjs --live --max-usd 0.3
node --env-file-if-exists=.env app-temp/checks/Validation/poc-note-update.mjs --live --max-usd 0.3
```

證據喺 `app-temp/data/validation/runs/2026-10-21-poc/`（git ignore）：每個模型一個 `notes*/`（`notes.md`、`report.md`、`segments.json`、`outline.json`、`concept-graph.json`、`live-jots.md`、`calls.json`），`asr/`（WAV、`results.json`、`report.md`），`course-context.json`。

**三速筆記流程**（每個 call 前面都係同一段課程 context，方便 prefix cache）：

1. **Course context**：由 `Tutorial.txt`（同一課程另一段錄音嘅機器字幕）抽 subject 同 15 個術語，連字幕聽錯嘅寫法（例如 `nt os kernel x` → `ntoskrnl.exe`）。真產品應該由 slides 抽。
2. **Live**：每分鐘 1–4 點，只睇嗰分鐘同之前三分鐘，唔決定擺邊。失敗嘅一分鐘留空，唔影響後面。
3. **Segment**：每五分鐘將嗰段寫成學習筆記，交 block、引用、概念、辨識修正紀錄。
4. **Final**：一次 outline（每個 block 放一次，最多三層）＋每個大題一次 polish（合併重複；丟咗兩成以上引用或一半以上字數就保留原文）。

## 結果 A：三速筆記（Week 3 全堂 164.6 分鐘，輸出 zh-Hant）

Baseline 係 2026-10-20 P2 嘅 `tree-notes-v15`（30 秒 batch＋organizer），同一份字幕。

| | Baseline Gemini 3.5 Flash-Lite | Baseline Qwen3.8 Flash | POC Gemini 3.5 Flash-Lite | **POC Qwen3.8 Flash** | POC Qwen3.7 Flash |
| --- | --- | --- | --- | --- | --- |
| 狀態 | 完成 | 133 分鐘時取消，冇保存 note | 完成 | **完成** | 失敗（見下） |
| Provider calls | 228 | ≥227 writer | 205 | 209 | — |
| Input／output tokens | 5,825,642／80,591 | — | 506,140／81,031 | 585,572／103,719（47% 命中 cache） | — |
| 費用（USD） | 1.949（list price 估算） | ≥0.63（OpenRouter，至 133 分鐘） | 0.354（list price 估算） | **0.099**（OpenRouter reported） | 0.044 |
| Topics（頂層），層數 | 127（8），3 | — | 19（8），2 | 12（12），1 | — |
| 筆記字數（不計附錄） | 46,591 | — | 28,424 | 49,300 | — |
| 「講師／lecturer」每千字 | 4.64 | — | 0.14 | 0.18 | — |
| 標「(?)」 | 147 | — | 5 | 62 | — |
| 有引用嘅分鐘 | 107／165 | — | 153／165 | **160／165** | — |
| Call 延遲中位數（最長） | 2.6 s（17 s） | 約 10 s | live 1.5 s；segment 4.2 s | live 6.7 s；segment 22.5 s（88 s） | — |

**觀察**

- **架構係主要分別**：同一個 Gemini 模型，input tokens 少 11 倍、費用少 5.5 倍、topics 由 127 減到 19，敘述式寫法（「講師提到…」）幾乎消失。Qwen3.8 全堂 US$0.099，舊 pipeline 跑到 133 分鐘已經 US$0.63。
- **Qwen3.8 Flash 內容最完整**：49k 字、160／165 分鐘有引用、聽唔清嘅字標「(?)」同時保留原本聽到嘅字（例如 `ntoskrnl.exe (? Transcript: "OS and kernel")`），比較符合 BR-03。缺點係 outline 冇用子題（12 個平排大題），同埋 segment 延遲中位數 22 秒（五分鐘一次仍然夠用）。
- **Gemini 結構最好但太有信心**：8 章 2 層，好似教科書目錄；但「(?)」得 5 個，例如字幕嘅「4069 個項目」（應係 4096）照抄冇標記；辨識修正表有錯對應（`needle dock → NtCreateFile`，應係 ntdll）。修正表一定要畀用戶睇同改。
- **Qwen3.7 Flash 唔適合**：佢喺 OpenRouter 嘅 endpoint 冇 `structured_outputs`，只可以用 JSON mode。Live 層大部分分鐘交唔到符合 schema 嘅回覆；segment 重試一次仍然唔啱，run 停咗。要用就要放寬 schema 或者用其他 parser。
- **兩個模型都分唔到段**：`continuesPrevious` 33 段之中 Gemini 32 段、Qwen 24 段話「接續上一段」，即係 segment 層嘅分段判斷冇用；結構主要靠 final outline。
- **輸入本身錯**：講師講嘅係 create file（見結果 B），字幕寫「query file」，所以 Qwen 筆記寫咗 `NtQueryFile`。筆記準唔準好大程度取決於 ASR。
- Qwen 引用會寫 `[T112-T115]` 範圍；app 而家嘅 citation parser 唔識，正式實作要處理。

## 結果 B：ASR 長窗口＋context（12:55、15:20、17:30 起各 90 秒）

Qwen3-ASR 1.7B（OpenRouter 上只有 DeepInfra）同 Whisper Large V3（DeepInfra、Together、Groq）各跑 8 秒、90 秒、90 秒＋prompt。OpenRouter 唔畀 transcription 指定 provider，prompt 經 `provider.options` 傳畀三個 provider（`groq`、`together`、`deepinfra/us`），再比較有冇改變輸出。Gemini 3.5 Flash-Lite audio 喺項目 ASR stack 以外，只作參考。（第一次跑時 OpenRouter 因 key 剩餘額度不足 US$0.50 回 HTTP 402；用戶將每月上限由 US$3 調到 US$5 之後重跑。）

| | 認啱術語（24 個） | 字幕常見錯字 | 字數 w1／w2／w3 | 3 段費用 | 每次延遲中位數 |
| --- | --- | --- | --- | --- | --- |
| 字幕（另一個 ASR） | 2 | 8 | 187／167／184 | — | — |
| **Qwen3-ASR 8 秒（而家 app 第一遍）** | **7** | **0** | 176／162／144 | US$0.0020 | 4.3 s |
| Qwen3-ASR 90 秒 | 6 | 1 | 181／164／151 | US$0.0020 | 32 s |
| Qwen3-ASR 90 秒＋prompt | 6 | 1 | 181／165／151 | US$0.0020 | 34 s |
| Whisper Large V3 8 秒 | 6 | 1 | 170／158／148 | US$0.0027 | 4.0 s |
| Whisper Large V3 90 秒 | 6 | 1 | 168／161／143 | US$0.0020 | 30 s |
| Whisper Large V3 90 秒＋prompt | 7 | 0 | 174／158／136 | US$0.0020 | 42 s |
| Gemini 90 秒（參考） | 8 | 1 | 184／170／153 | US$0.0037 | 34 s |
| Gemini 90 秒＋glossary（參考） | 10 | 0 | 163／154／**88** | US$0.0038 | 32 s |

- **Qwen3-ASR 8 秒已經係項目 stack 入面最好**：字幕嘅 `needle`、`Kevin debugger`、`Nptel`、`sample server` 錯字都冇，寫出 `ntdll.dll`、`NTCreateFile`、Win Debugger、LKD、symbol server。仍然有錯：create file 寫成「gray file／wave file」、Zw 寫成「setwprefix」、`ntdll!*` 寫成「NTDL. Animation wild card」。錯字 regex 要有空格先捉到（`set w`），所以「0」係低估。
- **90 秒窗口冇令 Qwen 或 Whisper 更準**（6–7 個），字數同 8 秒差唔多，冇好似 Gemini 咁漏尾；但延遲由 4 秒變 30 秒以上。三段樣本太少，唔可以話長窗口冇用，但亦冇證據支持 Session rerun 嘅 20–90 秒窗口會提升準確度。
- **經 OpenRouter 傳 context 對 Qwen 冇效**：w1、w3 有冇 prompt 輸出完全一樣，w2 只差兩三個字，即係 DeepInfra 冇用個 prompt。Whisper 有 prompt 時輸出唔同，但只係隨機變化，冇明顯改善（`O'Connor debugging`、`KWINDIBUG`、`ExterminationMap`），亦唔知邊個 provider 處理咗。
- **Whisper Large V3 明顯差過 Qwen3-ASR**：90 秒輸出有「colonel's debunking」、「Windy Bugger」、「Extermination map」、「WildcatCharacter」同斷開嘅碎句。
- **Gemini＋glossary 術語最好，但會靜靜提早收尾**：w3 只得 88 字，大約 40–50 秒講 Nt／Zw 同一地址嘅內容冇咗。用 LLM 做 ASR rerun 一定要有 guard（同第一遍比字數或時長、短啲嘅窗口），唔可以直接取代原文。
- 發現字幕原來錯咗意思：Gemini 聽到「I show you read file, but the slide is referring to create file」，即係 `NtCreateFile`；Qwen 8 秒同一句聽成「query file」，但後面又寫 `NTCreateFile`。要人手聽先確認。
- 費用：三段 4.5 分鐘音訊，項目 stack 六種方法合共約 US$0.013。Qwen3-ASR 每秒約 US$0.0000075，三小時約 US$0.08。
- 所有術語計數都冇人手聽過核對；`asr/w1.wav`–`w3.wav` 可以直接聽。

**對 ASR 方向嘅含意**：喺 OpenRouter 上，用課程詞彙去 bias Qwen3-ASR 做唔到。要 context biasing 有兩條路：(1) 直接接 Alibaba DashScope 嘅 Qwen3-ASR-Flash（官方 system message 收 background text／glossary，最長 5 分鐘），要新 key 同 adapter；(2) 唔郁 ASR，喺文字層用 glossary 修正：結果 A 嘅 segment 已經示範咗（Qwen3.8 記錄咗 131 個辨識修正，例如 `OS and kernel` → `ntoskrnl.exe (?)`），可以再加一個只改逐字稿顯示、保留原文嘅 transcript correction pass。第 (2) 條最平，亦符合 BR-03。

## 結果 C：用 graph 更新筆記（Qwen3.8 Flash）

[`poc-note-update.mjs`](../app-temp/checks/Validation/poc-note-update.mjs) 由結果 A 保存咗嘅 Qwen3.8 筆記開始。Graph 有兩層：**lineage**（引用：source → block → topic）同 **concept graph**（segment pass 抽出嘅概念 ↔ 提及佢嘅 block）。三速筆記嘅共用部分抽咗去 [`poc-notes-core.mjs`](../app-temp/checks/Validation/poc-notes-core.mjs)。

**情況 1：逐字稿要改。** 結果 B 嘅 Qwen3-ASR 90 秒 rerun（12:55、15:20、17:30）好似 Session rerun 咁取代 32 行字幕（變成 3 個新 source）。

- **Graph 更新**：lineage 搵到受影響嘅 segment（10:00–15:00、15:00–20:00）→ 只重寫呢兩段 → 新 block 放返舊 block 所在嘅 topic → concept graph 搵其他提及被改概念嘅 topic 做一致性檢查 → 只 polish 改咗嘅 topic。
- **完整重建**：全部 segment、outline、polish 重做（即 app 嘅 Rebuild）。

| | Graph 更新 | 完整重建 |
| --- | --- | --- |
| Provider calls | 4（segment 2、一致性 1、polish 1） | 47（segment 33、final 14） |
| Input／output tokens | 13,174／4,079 | 191,290／73,588 |
| 費用（OpenRouter reported） | **US$0.0037** | US$0.0534 |
| Provider 時間（各 call 加總） | **70 s** | 1,356 s |
| 改動以外嘅 topic 一字不改 | **11／11** | 0／11（outline 唔同咗） |
| 改動以外嘅筆記行仍然原文存在 | **582／582** | 9／582 |
| 仲引用被取代字幕行嘅地方 | 0（之前 22） | 0 |
| 受影響段落：字幕錯字 | `needle`、`set w`、`grave file` 消失；加咗 `NtCreateFile` | 同 graph 更新一樣 |

- **Lineage 係令更新可靠嘅部分**：用 1/14 嘅費用、1/20 嘅時間，得到同完整重建一樣嘅修正，而其他筆記完全唔郁。完整重建會換晒成份筆記（13 個新 topic，原文只剩 9 行），用戶嘅閱讀位置、編輯同 locked block 都要重新對位。
- 受影響段落仲有「OS and kernel」同「query file」：19:17 嗰句喺 rerun 窗口外，筆記寫 `ntoskrnl.exe (?)` 並保留原本聽到嘅字；「query file」係 Qwen3-ASR rerun 自己都咁聽，筆記寫「create file 或 query file」。兩樣都係合理行為。
- **Concept graph 做影響分析唔可靠**：重寫後「消失」嘅概念（`uf command`、`_KUSER_SHARED_DATA`、`Nt/Zw Prefix Equivalence`、`System Service Stub`）大部分只係改咗名（變成 `uf (Unassemble Function)`、`Zw vs Nt Prefixes`），唔係內容被推翻。佢搵到 1 個 topic（Process／PEB，經 `_KUSER_SHARED_DATA`），模型提議刪除嗰度一句有引用嘅筆記，理由係「概念被移除」，其實係錯；因為刪除會丟失引用 `[T505]`，guard 冇套用。結論同 app 嘅 STRUCTURE.md 一致：影響分析用 lineage，唔用概念名。

**情況 2：要求重寫一個 topic。** 指示：加入 Nt*／Zw* 比較表，並以「另見」交叉引用課堂其他部分（SSDT、SSN、ntoskrnl.exe）。

| | 只畀該 topic | 該 topic＋concept graph 連到嘅 10 個 block |
| --- | --- | --- |
| 有比較表 | 有 | 有 |
| 引用 topic 時間範圍外（超過 5 分鐘）嘅來源 | 0 | 2 |
| 「另見」內容 | IDT、`INT 2E`、`resides in this binary` 等，**冇引用**，即係模型自己補嘅知識 | SSDT（26:26–27:23）、SSN、ntoskrnl.exe（55:43），**引用到課堂原話** |

- **Concept graph 嘅價值喺 regenerate／問答時搵相關內容**：冇 graph 時模型只能用自己嘅知識寫交叉引用，冇得核對，違反「AI 補充同課堂事實要分開」；有 graph 就可以引用講師講過嘅地方。兩次合共 US$0.0049。

## 費用

Vertex（Gemini）約 US$0.58（list price × reported tokens，包括約 US$0.18 改 prompt 前作廢嘅回覆）；OpenRouter 約 US$0.22（筆記 Qwen3.8 US$0.099、Qwen3.7 US$0.044、ASR US$0.013、graph 更新＋重建＋regenerate US$0.059，當中一次 Alibaba「abnormal JSON」錯誤之後由 cache 續跑）。全部回覆已保存，offline 重跑唔收費。

## 結論

- **筆記**：三速架構係主要改善（費用、結構、唔再敘述「講師提到」）；Qwen3.8 Flash 內容最完整、最肯標「(?)」。Qwen3.7 Flash 喺 OpenRouter 冇 structured outputs，唔用。
- **ASR**：Qwen3-ASR 8 秒第一遍已經係項目 stack 最好；喺 OpenRouter 上長窗口同 glossary prompt 都冇改善。課程詞彙修正放喺文字層（segment pass 已示範）。
- **Graph**：lineage 決定更新範圍（便宜、穩定、同完整重建一樣準）；concept graph 用嚟搵相關內容（regenerate、Ask、概念索引），唔用嚟做影響分析。

要做嘅實作同未驗證項目已列入 [下一個 session 清單](next-session-checklist.zh-HK.md) 嘅「E. 三速筆記＋graph 更新」。
