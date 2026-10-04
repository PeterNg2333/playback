# 後端與檢查可讀性與結構 review（2026-10-03）

範圍：`app-temp/api`、`app-temp/checks`，以及原本放在 `api/` 的 `integration-check.mjs`。方法與 [前端 review](frontend-review.zh-HK.md) 相同：`/human-review`——先畫心智地圖，再逐檔檢查 file purpose 與 slop，最後按搬移清單重構。本文件同時是報告與結果：每項 finding 和搬移都標了「狀態」。文中的舊路徑以 commit `f1df1ac` 為準，對照時用 `git show f1df1ac:app-temp/api/<檔案>`。新結構的地圖見 [STRUCTURE.md](../STRUCTURE.md)。

## 心智地圖

產品概念（依 [business requirements](../../docs/business-requirements.md)）與它們的家：

| 概念 | 原本的家 | 現在的家 |
|---|---|---|
| Session、group、教材、語言設定 | `Db/PlaybackStore.cs`（634 行，全部 collection）、`Db/Models.cs`、`Services/LanguageSettings.cs`、`Endpoints/Contracts.cs` | `Db/Sessions.cs`、`LanguageSettings.cs`、`Endpoints/SessionsEndpoints.cs`、`GroupsEndpoints.cs` |
| Recording（錄音） | `Services/Audio/` 16 個平放檔之一 | `Audio/Recording/` |
| VAD | `Services/Audio/VadAudio.cs` 與 `SpeechActivityDetector.cs` 並排 | `Audio/Vad/` |
| ASR | `Services/Audio/` 內的 queue、processor、adapter 介面、兩個 provider、preparer、silence | `Audio/Asr/`，provider 在 `Audio/Asr/Providers/` |
| Audio chunk 與狀態 | `Db/PlaybackStore.cs`；狀態字串散在 6 個檔 | `Db/Chunks.cs`（`ChunkStatus`、`ChunkIdentity`） |
| Transcript 與翻譯 | `Db/PlaybackStore.cs`、`Services/Ai/Agents/Translation*.cs` | `Db/Transcripts.cs`、`Translation/` |
| Notes | `Services/Ai/`（NoteSections、NoteCoverage、NoteScheduler）、`Services/Ai/Agents/NoteAgent.cs`（436 行）、`Services/Ai/Providers/JevNoteGate.cs`、`Db/SectionNotes.cs`、`Db/NoteChangeLog.cs`、`Db/PlaybackStore.cs` | `Notes/`、`Db/Notes.cs` |
| Terms | `Terms/`、`Services/Ai/Agents/TermReviewAgent.cs`、`Services/Ai/Providers/JevTermClassifier.cs`、store 的 `TermInsightId` | `Terms/`、`Db/Terms.cs` |
| Ask | `Services/Ai/Agents/Chat*.cs`、`Db/Conversations.cs`、`Endpoints/QuestionsEndpoints.cs`、`Contracts.cs` 的 `QuestionInput` | `Ask/`、`Db/Conversations.cs`、`Endpoints/AskEndpoints.cs` |
| 引用與來源 | `Services/Ai/SourceReferences.cs`、`MaterialSources.cs` | `Sources/` |
| AI activity | `Services/Ai/AiActivity.cs`、`AiFlow.cs`、`Program.cs` 內嵌路由、`GroupsEndpoints` 的 flow 路由 | `Activity/`、`Endpoints/ActivityEndpoints.cs` |
| Provider | `Services/Ai/Providers/`（混有筆記與術語專用的 Jev 問題）、`Services/ProviderResponseReader.cs` | `Providers/` |
| 執行環境開關 | 15 個檔共 42 處 `Environment.GetEnvironmentVariable`，散落 `"5081"`、`"yes"`、`"no"` | `PlaybackEnvironment.cs` |

入口：原本 `api/` 只有 `Program.cs` 是明確入口，`Services/` 是機制名，看不出從哪裏讀起。現在 `Program.cs` → `Endpoints/`（一個資源一個檔）→ 概念 folder，每個 folder 在 STRUCTURE.md 有入口檔。

追蹤「錄音到筆記」：原本經 `CaptureEndpoints` → `Services/Audio/WindowsAudioCaptureService` → `Db/PlaybackStore.SaveLocalChunk` → `Services/Audio/AsrQueue` → `AsrProcessor` → `VadAudio`／`AsrAudioPreparer`／`SenseVoiceClient` → `PlaybackStore.SaveTranscript` → `Services/Ai/Agents/NoteAgent` → `Services/Ai/Providers/JevNoteGate` → `Services/Ai/NoteSections` → `Db/SectionNotes.SaveSectionNote`：四個 folder、十二個檔，筆記規則一半在 `Services/Ai/`、一半在 `Db/`。現在是 `Audio/Recording/` → `Db/Chunks.cs` → `Audio/Asr/` → `Db/Transcripts.cs` → `Notes/NoteAgent.Automatic.cs` → `Notes/NoteInput.cs` → `Notes/NoteSections.cs` → `Db/Notes.cs`，每一步的檔名就是概念名。

## Map breaks

- `Services/Audio/`：16 個平放檔，錄音、VAD、ASR、播放混音混在一起。
- `Services/Ai/` 依機制分（AI、Agents、Providers）：筆記散在四處；`JevNoteGate`（筆記問題）和 `JevTermClassifier`（術語問題）放在 Providers，不在用它們的概念旁。
- `Db/PlaybackStore.cs:17-634`：所有 collection 的讀寫，連同業務規則（ASR 最多試 3 次、翻譯 backoff、`NeedsReview`、`TermInsightId`）。`Db/Models.cs` 是 17 個 record 的雜物箱。partial 檔名 `SectionNotes.cs`、`SessionSync.cs` 不說自己是 `PlaybackStore` 的一部分。
- `Endpoints/Contracts.cs`：request record 與 endpoint 分家，但 `RetryChunksInput`、`CoverageRepairInput` 又在各自的 endpoint 檔；`ChatAgent`、`ChatContextBuilder`、`PlaybackStore.AddMaterial` 依賴 `Playback.Api.Endpoints`，服務層依賴 HTTP 層。
- Ask 一個概念三個名：`QuestionsEndpoints`、`ChatAgent`、`Conversations`。
- `Program.cs:47-48` 內嵌 activity 路由；group 的 AI flow 在 `GroupsEndpoints.cs:10`。
- 「gate」一字三義：Jev note gate、`NoteAgent.gates`（每 session 一個 semaphore）、`PlaybackStore.noteGates`（筆記寫入鎖）。
- `NoteSections.Hash` 被 `AiActivity`、`MaterialSources`、`SessionSync`、`PlaybackStore` 當通用 SHA-256 用。
- 錄音 chunk 長度 `ChunkMilliseconds` 定義在 ASR 的 `LiveAsrSession`。
- `AudioActivity` 與 `AiActivity`：activity 兩義；`AudioActivity.HasSound` 只剩 checks 呼叫，產品已改用 Silero VAD。
- `checks/Program.cs`：532 行，20 個 flag 分派加約 330 行內嵌 assertion（ASR、術語、筆記、翻譯、問答、grounding、Jev cache）。
- `NotesRedesignCheck` 以專案事件命名；partial class 分散在 `NotesRedesignCheck.cs`、`NoteCoverageCheck.cs`、`Week3LiveCheck.cs`，檔名與 class 對不上；fake 嵌在裏面，卻被付費 validation run 共用。
- `checks/` 根目錄 36 個檔：離線 check、要 MongoDB 的 check、付費 live check、寫 evidence 的 validation run、node launcher、PowerShell、Python 全部並排；`integration-check.mjs` 放在產品 folder `api/`。
- 8 個檔各自定義 `Check`／`Require`／`Reject`，其中四個丟 `InvalidOperationException`——放進「預期產品丟 `InvalidOperationException`」的 try/catch 時，失敗會被當成預期的拒絕。

## File purposes

只看檔名、folder 與第一屏寫出的猜測，讀完全檔後判定（舊路徑）。

| 檔案 | 猜測 | 實際 | 判定 |
|---|---|---|---|
| `Db/PlaybackStore.cs` | Mongo 連線 | 全部 collection 的讀寫加業務規則 | broke |
| `Db/Models.cs` | 資料模型 | 17 個 record，橫跨 8 個概念 | broke |
| `Db/SectionNotes.cs` | section 筆記 | `PlaybackStore` 的筆記版本 partial | broke |
| `Services/Audio/AudioActivity.cs` | 音訊活動偵測 | buffer 解碼、音量、已不用的能量閘 | broke |
| `Services/Ai/Agents/NoteAgent.cs` | 筆記 agent | prompt 文字、自動排程與 gate 狀態機、輸入組裝、生成、輸出驗證 | broke |
| `Services/Ai/NoteSections.cs` | 筆記 section | 文件模型，另加全專案用的 hash | broke |
| `Endpoints/QuestionsEndpoints.cs` | 問題 | Ask 與對話 | broke |
| `Endpoints/Contracts.cs` | API 合約 | 部分 request record | broke |
| `checks/Program.cs` | 入口 | 分派、330 行 assertion、Jev fake | broke |
| `checks/NotesRedesignCheck.cs` | redesign 檢查 | 筆記文件、排程、生成、同步檢查與共用 fake | broke |
| `checks/Week3LiveCheck.cs` | Week 3 live check | `NotesRedesignCheck` 的 partial，是付費 validation run | broke |
| `checks/SampleAudioPreview.cs` | 預覽 | 被四處共用的解碼 fixture，加一個 check | broke |
| 其餘 `api/`、`checks/` 檔 | — | — | held |

## Findings（依收益排序）

### [Blocker] `--sample-audio` 離線 check 在本 branch 起點已失敗 — `checks/SampleAudioOfflineCheck.cs:59, 75`

> 狀態：已修正（`checks/Offline/SampleAudioChecks.cs`）。現在通過：1,266 段、20 個 batch。

兩個過時預期：(1) 期望 prompt 含原始 transcript ID 與該段時間，但 Ask 早已改用短 alias 並按相鄰片段分組（`Source [03] (120000-180000 ms)`）；(2) 期望每批筆記輸入不超過 40 段，但 `NoteInput.Pending` 的上限是 64 段、18,000 字。修正後檢查包含該段的 alias 群組及其音訊範圍，以及實際上限；「Q&A 要帶音訊時間」這個要求沒有放寬。

### [Restructure] 以概念 folder 取代 `Services/` — `Services/Audio/`、`Services/Ai/{,Agents,Providers}/`

> 狀態：完成。

```text
Services/Audio/ 16 檔        →  Audio/Recording/, Audio/Vad/, Audio/Asr/ (+ Providers/), Audio/ 兩個共用檔
Services/Ai/ + Agents/ + Providers/  →  Notes/, Terms/, Ask/, Translation/, Sources/, Activity/, Providers/
```

收益：筆記從四個 folder 收到 `Notes/` 加 `Db/Notes.cs`；ASR adapter 按 owner 的規則放進 `Asr/Providers/`，外面只看到 `IAsrAdapter` 與 `AsrAdapters.Create()`；`Providers/` 只剩共用的 Gemini、Jev transport 與有界讀取。

### [Restructure] `Db/` 依保存的概念拆檔 — `Db/PlaybackStore.cs`、`Db/Models.cs`

> 狀態：完成。純搬移（腳本按成員行號切開，沒有改寫任何陳述式）；`PlaybackStore.Translate` 沒有呼叫者，刪除。

`PlaybackStore` 仍是一個類別，但每個 partial 檔是一個概念的文件與讀寫：`Sessions.cs`、`Chunks.cs`、`Transcripts.cs`、`Notes.cs`、`Terms.cs`、`Conversations.cs`、`Activity.cs`、`SessionSync.cs`、`TestData.cs`，`PlaybackStore.cs` 只剩連線（36 行）。這沿用了原本 `Conversations.cs`「record 加 partial」的寫法。業務規則搬到它描述的 record：`Transcript.NeedsReview`、`TermInsight.IdFor`；store 的 `TermInsight(...)` 改名 `FindTermInsight`（裸名詞會遮住同名 record）。

收益：`Db/` 最大的檔由 634 行降到 304 行（`Db/Notes.cs`）；「transcript 存了甚麼」只需開一個檔。

### [Simplify] 環境開關一個家 — 15 個檔共 42 處 `Environment.GetEnvironmentVariable`

> 狀態：完成。剩下的讀取只屬 provider 自己的設定（模型、endpoint、key）。

這是 accretion：每多一種模式就在用到的地方多一個字串比較，例如驗證 API 的 session 篩選在 `NoteAgent` 和 `TermReviewAgent` 各寫一次。

```csharp
// 原本（NoteAgent.cs:136-137，TermReviewAgent.cs 另有一份）
if (Environment.GetEnvironmentVariable("PLAYBACK_VALIDATION_PORT") == "5081")
    ids = ids.Where(x => x == Environment.GetEnvironmentVariable("PLAYBACK_VALIDATION_SESSION")).ToList();
```

```csharp
// 現在
ids = ids.Where(PlaybackEnvironment.AllowsAutomaticWork).ToList();
```

每個屬性每次呼叫都重新讀環境，所以 checks 在同一進程中途切換開關仍然有效。資料 folder 的 `"..", "..", "..", ".."` 三份計算也收進這裏。

### [Restructure] `NoteAgent` 依工作拆分 — `Services/Ai/Agents/NoteAgent.cs`（436 行）

> 狀態：完成。Prompt 文字與 HEAD 逐字相同；`NoteInput` 序列化的屬性順序不變，所以已保存的 gate 輸入身份仍然有效。

| 新檔 | 一句話 |
|---|---|
| `Notes/NoteInstructions.cs` | 筆記模型收到的指示及其版本 |
| `Notes/NoteInput.cs` | 模型看到甚麼：下一批來源、作 context 的 section、有界的 alias 輸入 |
| `Notes/NoteAgent.cs` | 明確的動作（Revise、Organize、Repair）與一次模型執行 `Revise` |
| `Notes/NoteAgent.Automatic.cs` | 何時自動執行：10 秒排程、Jev gate、保存的決定與重試 |

業務數字有了名字（`MaxSources = 64`、`MaxInputChars = 48_000`、`WaitsBeforeSafeguard = 3`、`RetryStep = 30 s` 等）；以例外訊息前綴控制流程的 `BoundedInput` 改為直接比較長度；`Generate` 的 `allowRevision` 參數（產品永遠傳 true）與 `MaterialsForPrompt` 的 `revisionOnly` 參數（產品永遠傳 false）刪除；semaphore 改名 `sessionLocks`，不再與 Jev note gate 同名。

### [Simplify] Chunk 生命週期與身份各一個家 — 狀態字串 6 個檔、ID 正則 5 處

> 狀態：完成。

`"pending-asr" or "transcribing" or "asr-error"` 在 `AsrQueue`、`NoteAgent`（兩次）、store 重複；session／source／hash 正則在 store、錄音、播放混音重複；`Audio(id)` 把 ID 字串拆回四段再拼路徑。

```csharp
// 現在（Db/Chunks.cs）
public static bool AwaitingTranscript(string status) => status is PendingAsr or Transcribing or AsrError;
public static string Id(string sessionId, string sourceId, long sequence, string hash) => $"{sessionId}-{sourceId}-{sequence}-{hash}";
```

ID 格式是已保存資料與 URL 的一部分，所以保留字串，只把格式與解析收進 `ChunkIdentity`。ASR 自動重試上限命名為 `ChunkStatus.MaxAutomaticAsrAttempts`；錄音 chunk 長度移到 `WindowsAudioCaptureService`，連同「3 秒後遇停頓提早封存」的兩個數字。

### [Restructure] Checks 依「需要甚麼」分組，以保護的行為命名 — `checks/`

> 狀態：完成。`Program.cs` 由 532 行變成 122 行的目錄。

| Folder | 需要 |
|---|---|
| `Offline/` | 甚麼都不用；`dotnet run` 不帶 flag 全跑，筆記檢查也併入預設 |
| `LocalMongo/` | localhost MongoDB（`playback_e2e`） |
| `Live/` | 付費 provider 加明確 opt-in |
| `Validation/` | 把 evidence 寫入 `data/validation/runs/` |
| `Fixtures/` | 共用的 in-memory store、fixture 模型與 gate、Jev HTTP fake、時鐘、合成講課、Week 3 解碼 |

`Offline/` 的檔以產品概念命名（`AskChecks`、`NoteSchedulingChecks`……），每組通過時印一行。唯一的 `Expect` 丟 `CheckFailed`，不會被「預期產品拒絕」的 catch 吞掉。兩個刻意的錯誤都令 checks 失敗：刪去「同一輸入不再問 Jev」的判斷，以及令術語解釋的身份忽略輸出語言。`api/integration-check.mjs` 移到 `checks/LocalMongo/api-integration.check.mjs`。

## 結構

### 原本 → 現在

```text
原本（f1df1ac）                          現在
api/                                     api/
  Program.cs                               Program.cs, PlaybackEnvironment.cs, LanguageSettings.cs, ContentHash.cs
  Endpoints/ (10)                          Endpoints/ (11)    一個資源一檔，含 request record 與錯誤回應
    Contracts.cs, QuestionsEndpoints.cs      AskEndpoints.cs, ActivityEndpoints.cs, ApiExceptionMiddleware.cs …
  Middleware/ApiExceptionMiddleware.cs     Db/ (10)           PlaybackStore 依保存概念分檔
  Db/ PlaybackStore 634 行, Models,        Audio/
      SectionNotes, SessionSync,             Recording/       Windows 擷取、來源模式
      Conversations, NoteChangeLog           Vad/             Silero
  Services/                                  Asr/ (+ Providers/)  queue、processor、adapter 介面、預覽
    LanguageSettings, ProviderResponseReader AudioSamples.cs, SessionAudioRenderer.cs
    Ai/ (7)  Agents/ (7)  Providers/ (5)   Translation/  Notes/  Terms/  Ask/  Sources/  Activity/
    Audio/ (16)                            Providers/         Gemini、Jev transport、有界讀取
  Terms/TermCandidateExtractor.cs          Resources/
  Resources/, integration-check.mjs
checks/ (36 個平放檔)                     checks/
                                           Program.cs, Expect.cs
                                           Offline/ (15)  LocalMongo/ (4)  Live/ (9)  Validation/ (16)  Fixtures/ (7)
```

`Validation/` 有 16 個檔，多於約十個的建議：它們是一次性的 evidence 工具（每個 run 一組 C# 加 node launcher），按 run 名前綴排列，暫不再分。

### 搬移清單

每一步之後都重新 build，並跑全部離線 checks。

1. **完成。** `Services/Audio/*` → `Audio/Recording/`、`Audio/Vad/`、`Audio/Asr/`、`Audio/Asr/Providers/`；`AudioActivity` → `Audio/AudioSamples.cs`（刪去只剩 checks 用的 `HasSound`）；`SessionAudioRenderer` → `Audio/`。（`Services/Audio` 平放、activity 兩義）
2. **完成。** `Services/Ai/*`、`Agents/*`、`Providers/*` → `Notes/`、`Terms/`、`Ask/`、`Translation/`、`Sources/`、`Activity/`、`Providers/`；`Db/NoteChangeLog.cs` → `Notes/`；`LanguageSettings.cs` → 根目錄；`Middleware/` 併入 `Endpoints/`。（機制分類、筆記四個家）
3. **完成。** `Db/PlaybackStore.cs`、`Models.cs`、`SectionNotes.cs` → 每概念一檔；刪 `Translate`。（store 雜物箱）
4. **完成。** `Contracts.cs` 拆回各 endpoint；`QuestionInput` → `Ask/ChatAgent.cs`；`AddMaterial(id, name, text)`；`QuestionsEndpoints` → `AskEndpoints`；新增 `ActivityEndpoints`。（合約兩個家、服務依賴 HTTP 層、Ask 三個名）
5. **完成。** 新增 `PlaybackEnvironment`。（開關 42 處）
6. **完成。** `ChunkStatus`、`ChunkIdentity`、`ChunkAudioPath`；錄音 chunk 規則移入 `WindowsAudioCaptureService`。（狀態與 ID 重複、ID 拆字串）
7. **完成。** `ContentHash.Of` 取代 `NoteSections.Hash` 與四處同樣的 SHA-256 寫法；`Transcript.NeedsReview`、`TermInsight.IdFor`、`FindTermInsight`；`ChatAgent` 的大寫 hex 收成 `QuestionHash`，註明已保存的對話以它比對。（規則放錯家）
8. **完成。** `NoteAgent` 拆成四檔（見上）；`NoteSections.Current(session)` 取代 pass-through 的 `NoteAgent.Sections`；`NoteAgent.AutomaticInterval` 別名刪除。
9. **完成。** `SessionSync.cs` 的上限命名；`SaveSectionNote` 一行一句（與 HEAD 去空白後逐字比對，只差鎖的名字與共用的 `usedCitations`）；`noteGates` → `noteWriteLocks`。
10. **完成。** 刪除沒有呼叫者或只剩 checks 呼叫的程式：`GeminiLanguageModel.OutputLimit` 的 `RollingLectureNoteEditor` 分支、`TranslationContext.Build`（單一目標版本）、`SaveNote` 未用的 `processedThroughMs`；`JevTermClassifier.Rank` 回傳 `JevRankResult`，不再要呼叫者轉型。
11. **完成。** Checks 重組（見上）；node、PowerShell、Python 腳本跟着它們啟動的群組搬；腳本內的相對路徑、`package.json`、README、`ai-flow`、`notes-chat-update`、STRUCTURE.md 已更新。`--notes-redesign-check` 改名 `--notes`。

## 未做

- `HealthEndpoints` 12 個永遠為 true 的 capability flag：同前端 review finding 4，`web/test/transcript-recording.check.mjs` 仍依賴舊路徑，要先改該 check。
- `/api/explain`、`/api/terms/evaluate-synthetic`（`SyntheticTermComparison`）沒有網頁或 check 呼叫；`/api/terms/rank` 只有 `Validation/validation-app.mjs` 用。刪 API 與檔案需要你決定。
- `NoteInstructions.Sections` 是逐次事故加句的 prompt（SLOP 的「prompt grows a sentence per incident」）。改寫會改變模型輸出，要有一次獲批准的 live 品質驗證才做。
- 筆記來源狀態（`"pending"`、`"deferred"`、`"completed"`……）與 transcript 的 `RecognitionStatus` 仍是字串，下一步可仿 `ChunkStatus`。
- `Notes/NoteSections.cs`、`Db/SessionSync.cs`、`Activity/AiFlow.cs` 仍有超過 140 字的行；它們有離線 checks 覆蓋，可下一輪一行一句。
- 沒有執行的 checks：三個 `LocalMongo/` check 與 `api-integration.check.mjs`（本機 MongoDB 沒有運行，起 container 需要你同意）；`Live/` 與 `Validation/` 的付費路徑。只確認了它們在缺 opt-in 或缺資料庫時於任何網絡請求之前停下。
- `Validation/validation-asr.mjs` 以 `OpenRouterAsrClient.cs` 的內容 hash 作 fingerprint；檔案搬了且 namespace 一行改了，舊的保存結果會被視為未有。本機沒有 `data/validation/`，目前沒有影響。
- `docs/` 下有日期的驗收報告保留當時的路徑與指令，屬歷史紀錄。

## Slop inventory

| Pattern | 位置（舊） | 次數 | 狀態 |
|---|---|---|---|
| 散落的環境開關字串 | 15 檔 | 42 | 收進 `PlaybackEnvironment` |
| 重複的 chunk 狀態集合 | `AsrQueue`、`NoteAgent`×2、store | 4 | `ChunkStatus.AwaitingTranscript` |
| 重複的 ID 正則 | store 三處、錄音、播放 | 5 | `ChunkIdentity` |
| 編入字串再拆回的 ID | `PlaybackStore.Audio` | 1 | `ChunkIdentity.WavPath` |
| 相同的 data folder 路徑運算 | store×2、錄音 | 3 | `PlaybackEnvironment` |
| 放錯家的 helper | `NoteSections.Hash` 在 6 檔使用 | 1 | `ContentHash` |
| 裸名詞成員 | `TermInsight(...)`、`Audio(id)` | 2 | `FindTermInsight`、`ChunkAudioPath` |
| 沒人設定的選項 | `allowRevision`、`revisionOnly`、`processedThroughMs` | 3 | 刪除 |
| 只剩 checks 使用的產品程式 | `HasSound`、`TranslationContext.Build`、`Translate`、`RollingLectureNoteEditor` | 4 | 刪除 |
| 以例外訊息控制流程 | `NoteAgent.BoundedInput` | 1 | 直接比較長度 |
| 以事件命名的 check | `NotesRedesignCheck`、「Repair checks」 | 2 | 以行為命名 |
| 各自定義的 assertion helper | checks 8 檔 | 8 | `Expect` |
| 一行多句 | `NoteAgent` 47 行、`SaveSectionNote` 等 | — | `NoteAgent` 與 `SaveSectionNote` 已改；見「未做」 |
| 未命名的業務數字 | `NoteAgent`、`SessionSync`、錄音、ASR 重試 | 約 25 | 已命名 |

## 刻意保留

- `Audio/Recording/WindowsAudioCaptureService.cs`（441 行）：一個概念（一次錄音的生命週期），線性易讀；只搬家並為 chunk 規則命名。
- `Db/SessionSync.cs` 的分頁、fragment 與 cursor 協定：是真實概念，有離線與 Mongo 兩套 check；只命名上限。
- `Notes/NoteChangeLog.cs` 的逐行 LCS：直白，有上限保護。
- `Audio/Vad/SpeechActivityDetector.cs`、`Providers/OutputGuardChatClient.cs`：小而專一，說明了為甚麼。
- ASR adapter 縫：`IAsrAdapter` 原本已乾淨，只把兩個實作放進 `Providers/`。
- `Endpoints/` 保持一個 folder：它是整個 HTTP 介面的目錄，一個資源一檔，比分散到各概念 folder 更容易一眼看完。
