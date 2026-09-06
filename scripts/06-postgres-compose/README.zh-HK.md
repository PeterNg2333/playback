# 06 · Local PostgreSQL + Docker Compose spike

這個 module 只回答一個問題：初學者能否在 Windows 用 Docker Compose 啟動一個只供本機使用的 PostgreSQL，理解 schema/query，再安全停止而不刪資料？它是學習 spike，不是 Playback 已接好的 runtime database。

## 你會學到

- image、container、volume、port mapping 分別是甚麼。
- 用 table、primary key、foreign key、constraint 表達資料規則。
- index 是為 query path 服務，不是每欄都加。
- 用 transaction 保持一組寫入全成或全不成。
- 在未來的 .NET/Npgsql code 用 parameters，而不是拼接 SQL。

## 安全邊界

| 項目 | 本 spike 設定 | 原因 |
|---|---|---|
| Image | `postgres:18.6-alpine` | 固定版本，避免 `latest` 漂移 |
| Host | `127.0.0.1` | 不向 LAN 公開 |
| Host port | `55432` | 避免與已安裝的 5432 衝突 |
| Database | `playback_spike` | 明示是 spike |
| User | `playback_demo` | 不使用真實帳戶 |
| Password | `playback_demo_password_change_me` | 故意公開的 demo password；絕不可重用 |
| Storage | named volume | Stop/recreate container 時保留資料 |

Docker Compose 官方文件警告：若 port mapping 不指定 host IP，會綁到所有 interfaces (`0.0.0.0`)；所以這裡明寫 `127.0.0.1:55432:5432`。[Docker Compose `ports` reference](https://docs.docker.com/reference/compose-file/services/#ports)

> 這個公開密碼只因 database 無法由本機以外連入而勉強適合教學。若日後開 LAN、多人使用或 deploy，必須改用 secret 管理、最小權限 user、TLS 與 authentication review。

## 四個檔案

```text
06-postgres-compose/
├─ compose.yaml      # container、loopback port、volume、healthcheck
├─ schema.sql        # 空 volume 第一次初始化的 relational schema
├─ queries.sql       # transaction、JOIN、parameters、EXPLAIN 練習
├─ study.ps1         # Check / Start / Status / Query / Stop
└─ index.html        # 可用 file:// 直接打開的教材
```

官方 `postgres` image 會執行 `/docker-entrypoint-initdb.d` 內的 `*.sql`，但**只在 data directory 為空時執行**。PostgreSQL 18+ image 的 persistent volume 應掛在 `/var/lib/postgresql`；Compose 已按這個規則設定。[Docker Official Image: postgres](https://hub.docker.com/_/postgres)

## 第一次操作

在 repo root 的 PowerShell：

```powershell
Set-ExecutionPolicy -Scope Process Bypass

# 只檢查工具與 Compose syntax；這是預設 action
.\scripts\06-postgres-compose\study.ps1

# 啟動並等 healthcheck 通過
.\scripts\06-postgres-compose\study.ps1 -Action Start

# 看 container / health 狀態
.\scripts\06-postgres-compose\study.ps1 -Action Status

# 插入一次獨立 demo session，執行 JOIN / PREPARE / EXPLAIN
.\scripts\06-postgres-compose\study.ps1 -Action Query

# 安全停止；不刪 container，不刪 volume
.\scripts\06-postgres-compose\study.ps1 -Action Stop
```

`Start` 使用 healthcheck 確認「ready」，因為 container 處於 running 不代表 database 已可接受 query。這是 Docker 官方對 startup order 的建議模式。[Docker: Control startup and shutdown order](https://docs.docker.com/compose/how-tos/startup-order/)

本 module 故意沒有 `Reset`。刪 named volume 會刪除資料，而且也會令 initialization scripts 再跑；在你理解 retention 與 backup 前，不把它包成一個方便按錯的命令。

## Docker 心智模型

```text
postgres:18.6-alpine image     唯讀的執行模板
            │
            ▼
postgres container            正在跑的 process / filesystem layer
       │             │
       │             └── 127.0.0.1:55432 → container:5432
       ▼
named volume                   database 真正持久資料
```

`Stop` 停 process，不動 named volume。`schema.sql` bind mount 是唯讀教材／初始化 input，不是 database data 本身。

## Schema：規則應由 database 也守住

```text
lecture_sessions (PK id)
    ├── audio_chunks (PK session_id + source_id + sequence)
    │       └── transcript_segments (FK 指回完整 chunk key)
    ├── note_revisions (FK session_id)
    └── lecture_materials (FK session_id)
```

### Primary key

一列的穩定 identity。`audio_chunks` 用三欄 composite primary key，令同一堂課、同一來源、同一 sequence 不能重複，這正是 retry idempotency 的 database proof。PostgreSQL primary key 同時要求 unique + not null，並自動建立 unique B-tree index。[PostgreSQL: Constraints — Primary Keys](https://www.postgresql.org/docs/current/ddl-constraints.html#DDL-CONSTRAINTS-PRIMARY-KEYS)

### Foreign key

保護關係：不存在的 session 不能有 audio chunk；不存在的 audio chunk 不能有 transcript。`ON DELETE CASCADE` 表示日後若有經授權的 session deletion flow，子資料也會一起清理。它不是讓 POC 自動刪資料。

PostgreSQL 不會自動替 foreign key 的 referencing columns 建 index，所以 schema 按預期查詢方向另建 indexes。[PostgreSQL: Constraints — Foreign Keys](https://www.postgresql.org/docs/current/ddl-constraints.html#DDL-CONSTRAINTS-FK)

### Index

Index 像書末索引，令特定 `WHERE` / `ORDER BY` lookup 少掃資料；代價是額外空間與每次寫入的維護工作。預設 `CREATE INDEX` 是 B-tree，適合常見 equality/range/order query。[PostgreSQL: Index Types](https://www.postgresql.org/docs/current/indexes-types.html)

`queries.sql` 會跑 `EXPLAIN`。只有幾列 demo data 時，planner 選 sequential scan 完全正常；不要用「看到 index 名」作唯一成功標準。

## Parameterized query，不拼接 user text

SQL template 與 value 分開傳送：

```csharp
await using var command = dataSource.CreateCommand(
    "SELECT title FROM lecture_sessions WHERE id = $1");
command.Parameters.Add(new() { Value = sessionId });
```

Npgsql 官方文件建議 PostgreSQL 原生 positional placeholder `$1`, `$2`；parameter value 不會被當 SQL 解讀，也可使用 prepared statement。[Npgsql: Parameters](https://www.npgsql.org/doc/basic-usage.html#parameters)

`queries.sql` 同時用 PostgreSQL `PREPARE` 示範 `$1`。`study.ps1` 傳給 psql 的 `:'session_id'` 是 psql 的安全 literal quoting 語法，目的是方便命令列課堂；未來 application code 應用 Npgsql parameter collection。

## 未來 .NET 連線字串（只供這個 demo）

```text
Host=127.0.0.1;Port=55432;Database=playback_spike;Username=playback_demo;Password=playback_demo_password_change_me
```

不要把真實 password 或 Gemini key 放進 `.cs`、tracked `appsettings.json` 或 SQL。正式 credential 應來自 user secrets / environment / secret store。

## 自我驗證

- [ ] `Check` 顯示 Compose syntax 有效。
- [ ] `Start` 等到 healthy，而非只看到 running。
- [ ] 由 host 只能用 `127.0.0.1:55432` 連入。
- [ ] `Query` 顯示一列 JOIN 結果及 query plan。
- [ ] 再跑一次 `Query` 會新增另一個 session，不破壞上次資料。
- [ ] `Stop` 後再 `Start`，舊資料仍存在。
- [ ] 你能指出每個 PK、FK、index 支援哪個規則／query。

## 官方參考

- [Docker Compose file reference: services / ports](https://docs.docker.com/reference/compose-file/services/#ports)
- [Docker Official Image: postgres](https://hub.docker.com/_/postgres)
- [Docker Compose startup and healthcheck](https://docs.docker.com/compose/how-tos/startup-order/)
- [PostgreSQL 18: Data Definition](https://www.postgresql.org/docs/current/ddl.html)
- [PostgreSQL 18: Constraints](https://www.postgresql.org/docs/current/ddl-constraints.html)
- [PostgreSQL 18: Indexes](https://www.postgresql.org/docs/current/indexes.html)
- [Npgsql: Basic usage and parameters](https://www.npgsql.org/doc/basic-usage.html#parameters)
