# 下一個 session：AI 筆記工作清單（2026-10-18）

目前 checkout：`main`，以 `9b92be0` 為本輪基底（原 local commits 保存在 backup branch）。2026-10-20 Wave 1 工作依使用者要求一併提交至本機 main；先看 [收尾與未達標項目](history/phases/wave1-closeout.zh-HK.md)。下一個 session 優先討論 cloud deployment／account issue，不自動開始 meeting profiles／FAQ。新增 paid tests 暫停，勿提高預算。

常用指令：
- Offline 檢查（唔上網）：`dotnet run --project app-temp/checks`；只跑筆記：加 `-- --notes`
- Live 模型比較（會收費，要明確 opt-in）：`npm run test:note-models-live -- --models "<candidate>,…" --folder app-temp/data/validation/runs/<名稱>`
- Candidate 格式：`openrouter:qwen/qwen3.8-flash@none|batch+organize`、`vertex:gemini-3.5-flash-lite|batch+organize`

## 已完成

- [x] 筆記以 code editor 式操作修改：細改自動套用，大改成為建議；可鎖定 block、留 comment、對全份／主題／block 下指令（commit `2566f4c`）
- [x] Jev note gate 預設關閉，改為 30 秒一批；六段／八分鐘係 review 門檻；相關內容可留喺已滿主題，再由 organizer 決定拆唔拆
- [x] 位置決定 harness（`tree-notes-v13`）：模型先喺 `outline` 宣告新主題（標題、上層），再將每段放入開放主題；只可以放喺過去 5 分鐘講過嘅主題下面；每個操作必須填 `topicId`、`blockId`
- [x] Organizer 用同一套 outline，只搬動／排序／改名；範圍外操作同冇標題嘅改名會被丟棄，唔會令成次失敗
- [x] 筆記模型預設 Qwen3.8 Flash（OpenRouter，Alibaba provider，有自動 cache），可用 `PLAYBACK_NOTE_MODEL` 改；冇 OpenRouter key 就用 Gemini
- [x] 繁體筆記用 OpenCC `s2hk` 轉換模型夾雜嘅簡體字（只轉純簡體字）
- [x] Live 比較（Week 3 頭 20 分鐘）：Qwen 58/58 來源、$0.027、四個合理大部分；Gemini $0.119、分組較亂。結果喺 `app-temp/data/validation/runs/2026-10-18-*`

## 待做

### A. 實測
- [ ] 實機三分鐘已做，Activity 顯示真實 Qwen／Alibaba；首 note 132 秒，未達約 40 秒。[P1 結果](history/phases/p1-results.zh-HK.md)
- [ ] 長測已取得部分證據；Gemini 109 分鐘停止，完整雙模型未完成。Week 3 只有 110,763 chars，未超過 200k，不能聲稱驗過滑動 window。[P2 結果](history/phases/p2-results.zh-HK.md)
- [x] 合成廣東話文字：兩模型八分鐘，8/8 引用；語義擴寫問題另列，唔係廣東話 ASR 驗收。
- [x] Web build、全部指定 offline browser checks，同實際 saved API desktop／mobile／引用跳轉通過。

### B. 筆記質素
- [x] P3 organizer 明確 scope／目標，拒絕範圍外同過深 moves；不保證模型每次成功重組。
- [x] 唔再用句子自動生成 subtopic 標題；冇宣告時保留原主題供 review。
- [x] OpenCC 詞組（示范→示範）已處理並保留香港用法 checks。
- [x] P4 streaming 已實作，Activity 支援 live draft。
- [x] Qwen → GLM → Qwen → Gemini transient fallback 已實作，P4b 決定及限制有紀錄。
- [x] Runner 可選 raw／OpenCC，保存兩種證據，有 offline paired check 同付費 guard。

### C. 會議模式
- [ ] 以另一個 prompt profile 重用位置決定 harness（議程、決定、行動項目），結構規則同 organizer 保持一樣。

### D. 收尾
- [ ] 開 PR：`feature/note-edit-operations` → `main`。
- [x] `app-temp/checks/output/`（檢查產生嘅 WAV）已加入 `.gitignore`。

### E. 三速筆記＋graph 更新（2026-10-21 使用者決定）

方向：live jot → 五分鐘 segment → final outline／polish，筆記模型 Qwen3.8 Flash；lineage 決定更新範圍，concept graph 用喺 regenerate／Ask。證據同數字見 [POC 結果](poc-three-speed-notes-asr-context-2026-10-21.zh-HK.md)。

- [x] POC：Week 3 全堂三速筆記（Qwen3.8 US$0.099、160／165 分鐘有引用）、ASR 長窗口／glossary 比較、graph 更新 vs 完整重建（US$0.0037 vs US$0.053，其餘 11 個 topic 一字不改）、有／冇 concept graph 嘅 regenerate。Script 喺 `app-temp/checks/Validation/poc-*.mjs`，預設 offline。
- [ ] 人手聽 `app-temp/data/validation/runs/2026-10-21-poc/asr/w1.wav`–`w3.wav`（各 90 秒），確認 create file／query file 同術語計數。
- [x] App：每分鐘 live jot，取代咗 30 秒 placement writer（`NoteWorkflow`，commit 2ccb53e）。
- [x] App：每五分鐘 segment 寫 block、概念、辨識修正；Stop 後 outline＋每個大題 polish。沿用 NoteTree、citations、locked blocks、CAS 同 history。舊 placement／organizer、coverage、processing settings 已刪。
- [ ] Outline 要強制分子題（Qwen3.8 出咗 12 個平排大題；2026-10-22 live 驗收 9–10 個，仍然冇子題）；citation parser 識 `[T112-T115]` 範圍（已做，`NoteAliases`）。
- [x] 逐字稿改動（Session rerun、第二遍 ASR）：窗口 hash 變咗先重寫嗰個 segment，只重新 polish 引用佢嘅 topic；Rebuild 只做明確嘅用戶動作。
- [x] Live 驗收（2026-10-22）：Week 3 全堂經 `NoteWorkflow`＋`playback_e2e`＋真 writer，US$0.164（錄音＋Stop US$0.108、改一行 US$0.0015、Rebuild US$0.055），161／165 分鐘有引用，Stop → outline 52 秒、全部完成 187 秒；改一行只重寫一個窗口、其餘 8 個大題一字不改；Rebuild 保留鎖定 block；網頁 chip 正常。可重用 check：`pnpm.cmd test:three-speed-live`（預設 offline）。[結果](three-speed-notes-live-2026-10-22.zh-HK.md)
- [ ] Live 驗收發現：Qwen3.8 有時段性 429（今次約三成 call 由 GLM 寫，GLM 筆記質素未評）；「(?)」239 個（POC 63），多數喺 Windbg 指令同數字；Rebuild 逐個窗口寫要 15 分鐘，可以並行。
- [x] 大題 polish 超時（live 驗收：14.6k 字嘅大題 Qwen 過咗 100 秒，Gemini fallback 又過唔到 guard，成個大題冇潤飾；Rebuild 亦有一個 polish 用咗 97 秒）：每個 polish call 最多 6,000 字筆記（Qwen 大約 3k output token、40–45 秒），長嘅大題喺 block 之間拆成長度相若嘅幾段，每段各自過 guard，失敗嗰段先保留 segment block。`ThreeSpeedNoteChecks` 覆蓋。代價：跨段嘅重複唔會合併。Live 複驗（`pnpm.cmd test:three-speed-live -- --finish`，同一份 Stop 前筆記嘅副本）：16 個 polish call 全部 16–41 秒、0 個到 deadline、0 個 fallback，15 個大題全部潤飾，Stop → 完成 157 秒，US$0.021。今次 outline 分咗 15 個較細嘅大題，只有一個大題拆成兩段，原本 32 個 block 嘅大題冇再出現。
- [x] 舊筆記資料：data patch `--legacy-note-patch`（`app-temp/checks/Patches/`），`Anchors` → block `SourceIds`，pre-tree 主題 → 一個 block；history 同 `note_heads.Current` 都改，舊欄位保留。2026-10-22 已套用喺本機 `playback_prototype`：781 個 notes＋9 個 heads（9 個 session），再跑一次 0 改動；API 睇 Lecture 5、Week 4、Week 3 全部 block 有來源。備份喺 `app-temp/data/backups/`（mongodump＋JSONL 原文）。
- [x] 第二輪（同日）：201 個只有 `Note.Markdown` 嘅 history 版本同 3 個 head（Test 1／3／4）按標題拆成 794＋12 個主題，來源由文中嘅逐字稿 ID 嚟；Lecture 5 嘅假引用（舊 writer 嘅 `T001…` alias 被寫成 `[cite_T009]`，或者 `[cite_new1]`）喺 87 個版本＋head 拎走 5,759＋106 個。104 個 alias 用 `ai_activity` 嘅 SourceIds 逐個核對過，佢哋嘅來源全部已經喺同一段嘅真引用入面。改咗 288 個 notes＋4 個 heads，再跑 0 改動；API 睇 Test 1／3／4、Lecture 5、Week 3 v1／v60／v145 全部 block 有來源。
- [ ] Concept graph：node key 用 glossary 嘅 canonical term（POC 入面概念改個寫法就當咗消失）；唔用嚟決定更新範圍。
- [ ] 課程 context：group＝course，glossary 由 slides 或上堂筆記建立、可以手改；辨識修正表要顯示同可以改（POC 有錯對應：`needle dock → NtCreateFile`）。
- [ ] 不確定數字：polish 唔可以將字幕數字當確定（POC：「4069 個項目」應係 4096，冇標「(?)」）。
- [ ] ASR：保持 Qwen3-ASR 8 秒第一遍；OpenRouter 上長窗口 rerun 同 glossary prompt 冇改善。逐 chunk 第二遍（`PLAYBACK_ASR_REVIEW`）停唔停要先量度；如要 ASR 層 biasing，先試 DashScope Qwen3-ASR-Flash 嘅 context。用 LLM 做 ASR rerun 一定要有長度 guard（Gemini 會靜靜漏尾段）。
- [ ] OpenRouter key 每月上限 US$5；剩唔夠 US$0.50 時所有 audio 請求（包括 app 嘅 ASR）都會 HTTP 402。

## 未來想做（產品方向）

每項開工前先對照 [business requirements](business-requirements.md) 同 [scope decisions](scope-decision.zh-HK.md)，再定範圍。

1. [ ] **Prompt 分組、事件觸發同筆記範本**
   - 按組別（session／workspace）揀 prompt 同筆記範本，例如上堂筆記、會議 mode（議程、決定、行動項目）。
   - 事件驅動嘅觸發流程：例如累積字數、STT 事件（講話停頓、換講者、Stop）觸發寫筆記或者其他 action；而家只有 30 秒批次、停頓同 Stop。
   - 基礎：`NoteWorkflow.Automatic`（分鐘／五分鐘窗口、Stop、閒置一分鐘）、`NotePrompts`（每個 task 嘅 prompt 同 schema）。
2. [ ] **按 session 上載 lecture notes 同文件**
   - 而家：`POST /api/sessions/{id}/materials` 只收純文字（name + text），筆記同 Ask 已經會用 materials。
   - 要加：上載 PDF／PPT／Word，抽取文字同頁碼，引用時可以指返原文件位置；限制檔案大小同類型。
3. [ ] **FAQ mode：估計我唔識乜**
   - 問我識同唔識邊啲，根據課堂內容同我嘅答案，預測我可能唔明嘅概念，整理成 FAQ（附來源）。
   - 基礎：Ask Playback（`ChatAgent`，有引用）、術語（Jev + 解釋）。
4. [ ] **講者分離（diarization）同標記講者**
   - 而家冇 diarization；只分 microphone 同 system audio 兩個來源。
   - 要揀支援 diarization 嘅 ASR provider 或者另外跑 diarization 模型；俾使用者為講者命名，筆記同逐字稿顯示講者。會議 mode 特別需要。
5. [ ] **Login**
   - 而家：demo access（`api/Security/DemoAccess.cs`），任何用戶名用共用 demo 密碼開 workspace。
   - 要加：真正嘅帳戶（密碼 hashing 或者 OAuth）、session 擁有權同權限。
6. [ ] **ORM、DB 同 hosting（已決定方向，2026-10-18）**
   - 而家：.NET backend + MongoDB（約 22 個 collection，手寫 `MongoDB.Driver` 查詢同 BSON mapping，`infrastructure/Storage/` 約 1,200 行）；錄音 WAV 存本機 `app-temp/data/audio/`。
   - **DB：Neon PostgreSQL + EF Core（`Npgsql.EntityFrameworkCore.PostgreSQL`）**。Schema 同 migration 寫喺 code（效果似 Prisma；Prisma 本身係 TypeScript，.NET 用唔到）。App 用 pooled connection string（`-pooler`），migration 用 direct；保留 `sslmode=require&channel_binding=require`。Region：AWS Singapore。筆記樹可以存做 `jsonb`。
   - **錄音：Cloudflare R2，經 S3 相容 API**（一對 access key + secret；10 GB 免費、播放流量免費）。寫一個 audio store interface：本機用檔案或 MinIO，雲端用 R2；日後可換 Google Cloud Storage（HMAC key）。播放用有時限嘅 signed URL。現有錄音先寫 `.wav.part` 再改名，改為寫完先上載成 object `audio/<session>/<source>/<seq>-<hash>.wav`。
   - **Hosting：Cloud Run（asia-southeast1）**。現有設計要 1 個長開 instance（背景工作喺 process 入面），係最大成本；考慮改成事件觸發（例如 Cloud Tasks）。
   - 一齊要處理：登入 signing keys 放 DB（唔可以放 container 檔案系統）；AI activity 而家存完整 prompt（每次最多 20 萬字），會好快用晒 Neon 0.5 GB 免費額度，改為只存摘要或者放 R2。
   - 次序：先出分階段計劃（users／sessions → transcripts／audio → notes），清本機 MongoDB 前先列出目標 DB、會失去嘅資料同重建計劃，等使用者批准。參考：[Cloud Run 部署](cloud-run-deployment.md)。
