# 下一個 session：AI 筆記工作清單（2026-10-18）

Branch：`feature/note-edit-operations`。開始前先讀 `AGENTS.md`、`app-temp/STRUCTURE.md`、[模型比較](note-model-comparison-2026-10-18.md) 同 `app-temp/README.zh-HK.md`，再睇 `git status`。

常用指令：
- Offline 檢查（唔上網）：`dotnet run --project app-temp/checks`；只跑筆記：加 `-- --notes`
- Live 模型比較（會收費，要明確 opt-in）：`npm run test:note-models-live -- --models "<candidate>,…" --folder app-temp/data/validation/runs/<名稱>`
- Candidate 格式：`openrouter:qwen/qwen3.8-flash@none|batch+organize`、`vertex:gemini-3.5-flash-lite|batch+organize`

## 已完成

- [x] 筆記以 code editor 式操作修改：細改自動套用，大改成為建議；可鎖定 block、留 comment、對全份／主題／block 下指令（commit `2566f4c`）
- [x] Jev note gate 預設關閉，改為 30 秒一批；主題滿 6 段或講咗 8 分鐘就唔再加內容；organizer 按結構觸發（一個主題超過 6 段、同一層超過 6 個主題）
- [x] 位置決定 harness（`tree-notes-v13`）：模型先喺 `outline` 宣告新主題（標題、上層），再將每段放入開放主題；只可以放喺過去 5 分鐘講過嘅主題下面；每個操作必須填 `topicId`、`blockId`
- [x] Organizer 用同一套 outline，只搬動／排序／改名；範圍外操作同冇標題嘅改名會被丟棄，唔會令成次失敗
- [x] 筆記模型預設 Qwen3.8 Flash（OpenRouter，Alibaba provider，有自動 cache），可用 `PLAYBACK_NOTE_MODEL` 改；冇 OpenRouter key 就用 Gemini
- [x] 繁體筆記用 OpenCC `s2hk` 轉換模型夾雜嘅簡體字（只轉純簡體字）
- [x] Live 比較（Week 3 頭 20 分鐘）：Qwen 58/58 來源、$0.027、四個合理大部分；Gemini $0.119、分組較亂。結果喺 `app-temp/data/validation/runs/2026-10-18-*`

## 待做

### A. 實測
- [ ] 用 `npm run dev` 實機錄一段：錄音 → ASR → Qwen 筆記 → organizer。確認 Activity 顯示 openrouter／qwen、筆記大約 40 秒內出現。
- [ ] 長課堂 replay（Week 3 完整 `transcript.txt`）：lecture window（200k 字）向前滑動時 cache prefix 會變，量度成本、延遲同 cache 命中率。
- [ ] 廣東話課堂 replay：分組、「(?)」標記、繁體字。
- [ ] Web build 同 browser checks（今輪冇改 web，但 Activity 嘅 provider 顯示來源改咗）。

### B. 筆記質素
- [ ] Organizer 常想改範圍外主題（一次有 7–8 個操作被丟棄）：考慮同一層太多主題時俾佢成個 outline，或者範圍包埋上層。
- [ ] 自動開嘅 subtopic（主題已滿時）標題仍似句子，要等 organizer 改名。
- [ ] 全部由共用字組成嘅簡體詞（例如「示范」）未轉；可考慮按 OpenCC 詞組轉換，但要保留香港用法（例如「了解」）。
- [ ] Qwen 每次約 10 秒：OpenRouter 改用 streaming，令 live draft 即時顯示。
- [ ] OpenRouter 失敗時冇自動改用 Gemini；決定要唔要 fallback。
- [ ] 比較 runner 冇經 OpenCC（量度模型原始輸出）；如要量度最終筆記，喺 runner 傳 `script`。

### C. 會議模式
- [ ] 以另一個 prompt profile 重用位置決定 harness（議程、決定、行動項目），結構規則同 organizer 保持一樣。

### D. 收尾
- [ ] 開 PR：`feature/note-edit-operations` → `main`。
- [x] `app-temp/checks/output/`（檢查產生嘅 WAV）已加入 `.gitignore`。

## 未來想做（產品方向）

每項開工前先對照 [business requirements](business-requirements.md) 同 [scope decisions](scope-decision.zh-HK.md)，再定範圍。

1. [ ] **Prompt 分組、事件觸發同筆記範本**
   - 按組別（session／workspace）揀 prompt 同筆記範本，例如上堂筆記、會議 mode（議程、決定、行動項目）。
   - 事件驅動嘅觸發流程：例如累積字數、STT 事件（講話停頓、換講者、Stop）觸發寫筆記或者其他 action；而家只有 30 秒批次、停頓同 Stop。
   - 基礎：位置決定 harness、`NoteInstructions`、`ProcessingPolicy`、`NoteAdmissionPolicy`。
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
