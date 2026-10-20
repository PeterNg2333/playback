# PostgreSQL、R2 同 Cloud Run 遷移計劃

查閱日期：2026-10-20。P5 文件交付；未安裝 EF、未建立雲端資源、未遷移或清除原資料庫。方向沿用已決定嘅 Neon PostgreSQL + EF Core／Npgsql、Cloudflare R2、Cloud Run `asia-southeast1`。

## 1. 現有資料同對應去向

以下係 `infrastructure/Storage/*.cs` 裏面 22 個具名 collection 嘅 source inventory，唔係對原資料庫做過 live export。ID、owner、原文、時間、hash 同版本全部保留；新 table 用原 string ID 做 primary key，唔重新編號。

| Mongo collection → PostgreSQL table | 用途／現有讀寫檔案 | Key、index 同要保留嘅規則 |
| --- | --- | --- |
| `users` → `users` | `Users.cs`：demo username upsert | 舊 username 目前係 `_id`；新帳戶另有不可改 user ID，username 唯一；舊 owner mapping 唔可以靠重複登入自動認領 |
| `sessions` → `sessions` | `Sessions.cs`、`PlaybackStore.cs`、`Chunks.cs`：建立、列表、owner guard | PK id；owner+created、group 索引；所有查詢保留 owner filter；group 必須屬同一 owner |
| `groups` → `groups` | `Sessions.cs`、`PlaybackStore.cs` | PK id；owner 索引；刪 group 只解散關聯，唔刪 session |
| `materials` → `materials` | `Sessions.cs`、`Notes.cs`、`SessionSync.cs` | PK id、session index；保留原文字，將來另加 page/slide/paragraph 定位 |
| `chunks` → `audio_chunks` | `Chunks.cs`、`Transcripts.cs` | PK 舊 chunk id；UNIQUE(session,source,sequence,hash)；session+start、status+recorded indexes；重試沿用身份；改 hash 係另一個內容，唔靜默覆蓋 |
| `audio_streams` → `audio_streams` | audio repository adapters、`SessionDeletion.cs` | PK stream id、session+source index；保留 sample format／pause／resume 邊界 |
| `combined_audio` → `combined_audio` | audio projection adapters、`SessionDeletion.cs` | PK manifest id；children、offset、hash 用 jsonb；原 chunk 唔刪，重建冪等 |
| `speech_boundaries` → `speech_boundaries` | audio boundary adapter、`SessionDeletion.cs` | PK boundary id、session+time index；同一個 pause/Stop 唔重複觸發 |
| `transcripts` → `transcripts` | `Transcripts.cs`、`Notes.cs`、`SessionSync.cs` | PK chunk id；session+start、note_status+session indexes；原 ASR、provider、語言同引用身份不變；display 唔覆蓋 original |
| `translation_versions` → `translation_versions` | `Transcripts.cs` | PK 原版本 id；UNIQUE(來源、輸入 hash、語言、版本身份)；舊翻譯只新增版本，唔覆寫原文 |
| `notes` → `note_versions` | `Notes.cs` → `MongoNoteRepository`／`MongoNoteHistoryStorage` | UNIQUE(session,version)；tree/citations/dispositions 用 jsonb；歷史不可變、分頁排序照舊 |
| `note_heads` → `note_heads` | `Notes.cs` → history journal | PK session；expected-version CAS；head 同 history 於一個 transaction 寫入；匯入前先處理 pending-history marker，唔遺漏已提交 head |
| `note_gates` → `note_gates` | `Notes.cs` | PK session；flush_version、deadline、retry、輸入 hash 原樣保留；舊 acknowledgement 唔可以清走新 Stop |
| `note_organization` → `note_organization` | `NoteOrganization.cs` | PK session、revision concurrency token；保存 policy snapshot、pending scope、ReviewedStructures 同重啟 checkpoint |
| `note_rewrites` → `note_rewrites` | `NoteRewrites.cs` | PK proposal id、session+status index；based-on block/hash 同保護狀態照舊；stale proposal 拒絕 |
| `processing_settings` → `processing_settings` | `ProcessingSettings.cs` | PK workspace/session scope；revision CAS；繼承 revision 唔一致回 conflict，唔覆寫草稿 |
| `term_insights` → `term_insights` | `Terms.cs`、`Sessions.cs` | PK insight id、session index；保留來源集合、模型決定同 explanation hash |
| `term_explanation_history` → `term_explanation_history` | `Terms.cs` | PK archive id；保留舊說明及來源；保存新版時先確保歷史可重試 |
| `conversations` → `conversations` | `Conversations.cs` | PK id、session+updated index；私人對話 owner 唔應只靠公開 session 身份 |
| `conversation_turns` → `conversation_turns` | `Conversations.cs` | UNIQUE(conversation,request_id)；同 request id 不同 input hash 拒絕；answer 可用 jsonb |
| `citations` → `web_citations` | `Conversations.cs` | PK id、session+question index；保留 URL、來源種類同字元 offsets |
| `ai_activity` → `ai_activity` | `Activity.cs` | PK activity id、session+started DESC index；保留 actual model/provider、usage、狀態；大 prompt 改 hash／有界摘要或 R2 物件 |

現有明確建立嘅 secondary indexes 喺 `StorageIndexes.cs`：`note_session_version`、`chunk_recovery`、`chunk_session_time`、`transcript_session_time`、`note_evidence_recovery`。Mongo 自動 `_id` unique 同 app CAS 唔係上述 secondary indexes；Postgres 要明確表達兩者。

共用規則：DB `timestamptz` 用 UTC；時間 offset/sample count 用 `bigint`；不以浮點數儲存版本／身份。Owner/session FK、unique constraint 同 transaction 係資料庫約束，唔只靠前端。對應 EF concurrency token 行為見 [Microsoft concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)。

## 2. 實作階段同驗收

### 階段 1：users／sessions／groups，同真正帳戶基礎

先加 Infrastructure 嘅 `PlaybackDbContext`、entity mapping、migration 同 EF/Npgsql 10.x 相容固定版本；實作前由 NuGet 官方頁選實際 patch 並鎖定，今輪唔安裝。`IWorkspaceRepository`、recording ownership port 保留 application 合約。改 `Storage/Users.cs`、`Sessions.cs`、`PlaybackStore.cs`、`ProcessingSettings.cs`、`ServiceRegistration.cs` 嘅對應讀寫；所有依賴 Mongo session/group 查詢嘅位置一齊經共用 workspace port，唔可以留下「建立咗 Postgres session，但舊 chunk guard 去 Mongo 搵唔到」嘅混合狀態。

先匯入／驗證同一 ID、owner、group 關聯，再一次切換呢三個 aggregate 嘅 authority。其餘 collection 暫用 Mongo。跨兩個 DB 嘅刪 session 無法靠一個 SQL transaction 包住：先持久化 deletion intent，阻止新工作，再逐 adapter 冪等清理、最後移除 session；唔加入不可靠 dual-write。

完成條件：兩個獨立帳戶建立／列表／移 group／重啟後重載成功；跨 owner read/write/audio 均拒絕；舊 notes/transcripts 可經相同 session ID 讀到；舊 user identity 有對應清單；offline checks 同 Postgres workspace checks 綠。帳戶具體登入選擇見最後一節，未決定前唔自動將共用 demo 帳戶公開。

Rollback：切換前保留完整 Mongo export、固定舊 image、對照每條 record hash；切換後如有新寫入，要先停寫同反向匯出新增／修改記錄，唔直接切返一個已過時嘅 Mongo。

### 階段 2：transcripts／chunks／audio

保持 `IChunkRepository`、transcription repository 同 recording/audio ports；改 `Chunks.cs`、`Transcripts.cs`、audio repositories、播放 endpoint 同 ServiceRegistration。新增一個小 `IAudioObjectStore` adapter：local files 供 Windows 開發；R2 供雲端。錄音 client 仍先保存 `.wav.part`，完成 rename 先上載；R2 唔代替本地持久 queue。

物件 key 固定 `audio/<session>/<source>/<sequence>-<sha256>.wav`。驗證實際 hash／長度後，PUT 成功先標記 metadata ready。相同 key 重試先核對 hash metadata；唔用 multipart ETag 當 SHA-256。DB+R2 無共用 transaction：pending-upload/outbox 記錄同 chunk metadata 同交易提交，worker 重試 upload，再 CAS ready；定時對帳孤兒／未完成物件。刪錄音用相同 outbox，唔先刪原檔。

播放：先檢查 owner，才產生短效（建議五分鐘）GET signed URL；URL 不保存到 DB／localStorage／log。用 S3 API hostname，同時配置所需 CORS、Range，驗證拖曳播放及過期後重新取 URL。R2 官方 [presigned URLs](https://developers.cloudflare.com/r2/api/s3/presigned-urls/)、[upload／multipart](https://developers.cloudflare.com/r2/objects/upload-objects/)。

完成條件：offline chunk identity/retry、hash mismatch、upload 後斷線、重啟補傳、同 sequence 不同 hash、Range、跨 owner 拒絕；Postgres 對應 `LocalMongo/SessionSyncStoreChecks`／audio projection checks；本機原 WAV 可回聽。R2 live 測試只用新 bucket prefix，列明物件數同成本後另行執行。Rollback 保留舊檔同 metadata 對照；只有 readback/hash 齊全先批准刪舊副本。

### 階段 3：notes／versions／terms／chat／activity

用 SQL transaction 將 expected-version head 更新、immutable version insert 同 provenance/dispositions 一齊提交；匯入先 reconcile Mongo pending-history。相同版本身份重試不新增，stale base 仍回 conflict。筆記樹初期可用明確 jsonb payload 保留整棵 immutable tree；若需要 EF 查 tree 欄位，EF 10 complex type `ToJson()` 係 Npgsql 官方建議，唔用已棄用嘅透明 POCO JSON mapping：[Npgsql JSON](https://www.npgsql.org/efcore/mapping/json.html)。

檔案：`infrastructure/Notes/MongoNote*` 嘅 Postgres 對應、`Storage/Notes.cs`／`NoteOrganization.cs`／`NoteRewrites.cs`／`Terms.cs`／`Conversations.cs`／`Activity.cs`／sync projections、ServiceRegistration。保留 `INoteRepository` 等現有 ports；一次只切一組 authority，唔另寫一套 application workflow。

完成條件：Postgres 版 `NoteStoreChecks`（125 versions、restore 新增第 126 版、citations）、conversation request-id checks、session sync store checks；競爭寫入只得一個 CAS winner；在 head/history 寫入點注入故障後，重啟冇遺漏／重複；同 fixture browser 對比可觀察結果。Rollback 方法同階段 1，對新 note versions 必須完整反向匯出。

每階段 migration 先喺全新 test DB 演練，再做 reviewed SQL／migration bundle；唔喺每個 Cloud Run instance 啟動時自動 migrate。[Microsoft production migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)。

## 3. 本機同 Neon 連線

建議先用本機 Docker Postgres（另開未使用嘅 loopback port、新 volume），store checks 可離線重現、唔耗 Neon compute；缺點係要維護 Compose 同重做雲端連線驗證。Neon dev branch 容易同雲端對齊／隔離，但依賴網絡、憑證同計費，唔可以成為預設 offline checks。

正式 app 用 pooled hostname；migration、備份用 direct hostname；Npgsql connection string 由 process environment 讀，唔直接將 PostgreSQL URI 當成 .NET key/value connection string。採用供應商要求嘅 TLS／channel binding 設定並以 Npgsql 相容語法驗證；不在文件放真實連線資料。Transaction pool 不可依賴 session-level `SET`、`LISTEN` 或 advisory lock：[Neon pooling](https://neon.com/docs/connect/connection-pooling)。

## 4. 雲端一齊要處理嘅事

Data Protection key ring 係 cookie 解密用，唔係 API key。用 EF `PersistKeysToDbContext`，各 instance 共用固定 application name；持久 key 另以適當 KMS／certificate 保護，讀寫身份最小化；驗證 container 重啟後原登入仍可讀。[Microsoft key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0)。

Activity 容量推算（設計估算，唔係已量度 DB 大小）：三小時每 30 秒一批最多 360 writer calls；每次 200k UTF-8 ASCII bytes 單係 prompt 就 72 MB；中文字最壞約 216 MB，未計 index／notes／回覆。以舊 0.5 GB 假設約得 2–6 堂。改每 call hash+最多 2 KB 摘要，360 calls 約 0.72 MB 加其他資料；必要 raw evidence 放 R2，保留 object key／hash／期限。Neon quota 以開項目時嘅 [官方 pricing](https://neon.com/pricing) 為準，唔保證舊 0.5 GB 免費額度仍適用。

現有 hosted workers 需要 request 外有 CPU。第一個雲端驗證可先一個 instance、instance-based billing、min/max 1，仍需重啟恢復同 durable work state；min instance 唔係永不重啟承諾。[Cloud Run billing](https://docs.cloud.google.com/run/docs/configuring/billing-settings)。估價公式 `730*3600*(vCPU*regional_cpu_rate + GiB*regional_memory_rate)`，再扣適用免費額度，另加 DB／R2／模型／網絡。以公開 $0.000018/vCPU-s、$0.000002/GiB-s 作參考，1 vCPU/0.5 GiB 約 $49.93/月未扣額度；呢個係公式示例，Singapore region SKU 必須用 [pricing calculator／region table](https://cloud.google.com/run/pricing) 重新確認，唔當成報價。

後續可用 Cloud Tasks 令持久 work item 以 OIDC-authenticated HTTP 觸發，request 只喺保存完成後 ACK；transaction outbox、防重 request identity、deadline、retry、per-session serialisation 都要有；唔只將 `BackgroundService` 搬入 fire-and-forget handler。[Cloud Tasks + Cloud Run](https://docs.cloud.google.com/run/docs/triggering/using-tasks)。R2 標準儲存定價同免費項目見 [官方 pricing](https://developers.cloudflare.com/r2/pricing/)，唔將免 egress 當成所有操作免費。

## 5. 任何清 DB 前嘅批准文字

> 我準備清除 **[實際 host:port／container／volume／database]**。將失去 **[users、sessions、逐字稿、notes versions 等確切範圍及 record 數]**；錄音檔 **[保留位置或刪除範圍]**。備份係 **[export 路徑、hash、readback 結果]**。重建會用 **[migration ID、seed／import 指令]**，驗證 **[counts、IDs、owners、引用、播放]**，失敗以 **[具體 rollback]** 還原。只影響以上 target，其他 DB／volume 不動。請明確批准以上清除及重建。

未有 live target／record inventory 前，以上只係模板，唔係批准請求，亦唔代表已有權清 DB。

## 6. Wave 1 之後：cloud／accounts 次序

使用者 2026-10-20 指示：Wave 1 後優先 cloud deployment 同帳戶，唔自動開始會議模式／FAQ。建議下一個小範圍係階段 1 加真正 account ownership，同時準備 deployment draft。

登入方式到開工時揀密碼或指定 OAuth provider；帳戶需要不可改 ID、username/email 唯一、密碼 hashing 或 provider-subject mapping、登出／cookie invalidation／跨 owner checks。Legacy demo username 唔可以由第一個新註冊同名帳戶自動認領。共享／邀請另定權限，私人問答仍私有。

Cloud Run Linux 冇 Windows WASAPI：部署前必須決定保留 Windows 本機 capture client 連雲端，或先實作 browser capture + 本地 durable upload queue。現有 React 控制嘅係同機 API，唔可以以為將 API 放雲端就錄到使用者電腦聲音。部署草案要列 image、region、service、runtime identity、secrets names、DB/audio target、health、cost、rollback 同未驗證功能，待一次具體確認才部署。
