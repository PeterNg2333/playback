# 07 · 可行性結論與 POC 實作 Roadmap

## 結論先講

這個構想**值得做 POC，技術上可行**，但目前 repo 的 `scripts/00`–`07` 是 spike study，不是已完成 MVP。最需要先證明的不是 UI 或 LLM 文筆，而是三小時錄音在 page reload、server/DB/API failure 下仍有完整、可重跑的 audio。

建議順序是：

1. Windows Audio Client 可靠落地分段 WAV。
2. SenseVoice adapter 能追上錄音速度並可安全重試。
3. AI Server 持久接收 chunk、排序 transcript、提供 snapshot。
4. 才加入每 5 個 active minutes 的 rolling note。
5. 再加入基於當下 snapshot 的 live Q&A 與 citation search。
6. 最後做三小時 soak/fault test，再決定是否值得 productize。

這個排序令最危險的假設最早失敗，不會先花時間做漂亮 UI，最後才發現 audio 不完整。

## Current-product landscape（快速定位，不是市場報告）

以下只根據產品官方頁面，查閱日期為 2026-09-06。功能、方案及平台限制會改；「未見官方承諾」不等於產品一定做不到。

| 產品 | Live transcript | Session 中 notes / Q&A | Material-grounded Q&A | Meeting bot dependency |
|---|---|---|---|---|
| Otter | 有 live transcript | 官方 Zoom 頁列出 live summary 及會中即時問答 | 官方首頁主張可查 meetings / connected apps；不等同任意 lecture material grounding | 不必然依賴：可派 meeting agent，也有 desktop/mobile bot-free capture |
| Notta | 有 real-time transcription | 有 AI notes；官方頁主張 cross-meeting Q&A，但本次查閱未找到「當前會議中 Q&A」的同等明確承諾 | 可跨 recordings/files 分析；是否在 live lecture 同步 grounding 未明示 | 不必然依賴：Notta Bot 或 Desktop bot-free 皆可選 |
| Granola | 有 desktop/iPhone live transcription | 可會中寫 raw notes，Chat 官方明示可在會議中提問；enhanced notes 通常在通話結束後生成 | Chat 可針對 meeting notes/transcripts，desktop 可另加 file context | 不依賴：desktop app 直接用 system audio + microphone，無 bot |
| NotebookLM | 官方流程是匯入 audio 後轉錄，不是 live recorder | 可對已加入 sources 提問及產生 study guide；不是本次查閱所見的 live meeting workflow | 強項：以 uploaded sources 回答並附 inline citations | 不適用；它是 source notebook，不是 meeting bot |

官方依據：[Otter product / live capture](https://otter.ai/) 與 [Otter for Zoom](https://otter.ai/zoom)；[Notta product](https://www.notta.ai/en/) 與 [Bot / Desktop capture options](https://www.notta.ai/en/features/notta-bot)；[Granola transcription](https://docs.granola.ai/help-center/taking-notes/transcription)、[during-meeting Chat](https://docs.granola.ai/help-center/getting-more-from-your-notes/chatting-with-your-meetings) 與 [enhanced notes timing](https://docs.granola.ai/help-center/taking-notes/ai-enhanced-notes)；[NotebookLM overview](https://support.google.com/notebooklm/answer/16164461?hl=en) 與 [audio/source import](https://support.google.com/notebooklm/answer/16215270?hl=en-GB)。

所以不能聲稱「市場上沒有同類」。Otter、Notta、Granola 已覆蓋 live transcription / notes / chat 的大量組合，NotebookLM 已覆蓋 material-grounded Q&A。Playback 值得驗證的差異化假設是：**不加入 meeting 也能由獨立本機 recorder 保存可重跑 audio，並在長 lecture 尚未完結時，把 transcript + lecturer material 連續整理成有 watermark/citations 的 rolling notes 與 Q&A**。這是假設，不是已證明的競爭優勢。

## 未來的 microservice concept

`microservice` 在這裡先表示清楚的 ownership boundary，不要求 POC 一開始就部署很多 containers：

```text
┌──────────────────────────────────────┐
│ Audio Client                         │
│ Windows first；未來可有 Native Mobile │
│ mic → local VAD → WAV chunk → spool  │
└──────────────────┬───────────────────┘
                   │ durable chunk upload + retry
                   ▼
┌──────────────────────────────────────┐
│ AI Server · ASP.NET Core             │
│ intake / ASR adapter / transcript    │
│ rolling-note agent / chat / search   │
└─────────────┬───────────────┬────────┘
              │               │
              ▼               ▼
        PostgreSQL       SenseVoice / Gemini /
                         allowlisted search provider

┌──────────────────────────────────────┐
│ UI                                   │
│ desktop/mobile browser：查看與提問     │
└──────────────────┬───────────────────┘
                   └──── same AI Server API
```

第一個 runnable POC 可以只有兩個 local processes：Audio Client + AI Server；plain Web UI 由 AI Server 靜態提供。等 profiling/ownership 證明有需要，才拆 ASR/agent workers。不要用「microservice」換來未被驗證的 deployment complexity。

## 為甚麼 desktop backend / native client 擁有錄音

Browser/PWA 適合 UI，不適合成為三小時 lecture audio 的唯一 source of truth：

- `getUserMedia()` 依賴目前 document、permission、secure context；document 非 fully active 可直接失敗。[MDN: `getUserMedia()`](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getUserMedia)
- Background/hidden tab 的 timer 會被 browser throttling；mobile browser 甚至可 unload page。[MDN: Page Visibility API](https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API)
- Microphone permission 被撤銷、hardware 被移除等情況會令 media track ended。[MDN: MediaStreamTrack `ended`](https://developer.mozilla.org/en-US/docs/Web/API/MediaStreamTrack/ended_event)

因此 Windows POC 由登入使用者 session 內的 Audio Client 持有 microphone。Page reload 只影響觀察畫面，不能自動 stop capture。

### 手機路線

首版手機只做 responsive UI：查看 transcript/note、提問、控制已連接的 recorder。這條路成本最低。

如果 lecturer 附近的手機要成為較佳收音來源，可靠版本應做 native app：

1. 在手機本地先寫完整 chunk。
2. 以 `(sessionId, sourceId, sequence)` 作 idempotency key。
3. 上傳失敗留在本機 queue，network 恢復再重試。
4. server 回 durable `STORED` receipt 後才可按 retention policy 清理。
5. 明確處理 iOS/Android background execution、mic permission、電量、儲存空間。

Live stream 可作低延遲 fast path，但不可是唯一 copy；local chunks 才是 recovery path。

## 三小時 lecture 的量級

若 POC 用 16 kHz、mono、PCM16 WAV：

- raw audio 約 `16,000 samples/s × 2 bytes = 32 KB/s`。
- 3 小時約 `345.6 MB`（約 `329.6 MiB`），另加 WAV header/metadata。
- 15 秒一段約 720 chunks。

這個量級在 laptop disk 合理，但足以暴露 queue、file handle、database/index、memory leak 與 retry 問題。測試不可只錄 2 分鐘後推論三小時也一樣。

## Step-by-step future implementation

### Gate 0 · 定義可觀察的 Demo / Real mode

**做甚麼**

- `Demo` 只用假 audio/ASR/LLM/search，所有輸出標明 demo。
- `Real` 才可送資料到公司 SenseVoice、Gemini、search provider。
- `/health` 顯示 mode、DB、Audio Client、ASR、LLM、search 是否 configured/ready。
- 設 retention、consent 提示與本機 data folder；API key 只從 environment/user secrets 讀取。

**通過證據**

- Missing `GOOGLE_AI_STUDIO_API_KEY` 會明確停用 note/chat，不會 silent mock fallback。
- UI 能逐項說明 audio、transcript、material、question/search query 會去哪裡。

### Gate 1 · Windows capture + durable local spool

**做甚麼**

- 在正常登入的 Windows user session 執行 Audio Client，不作 Session 0 service。
- 先用可選 microphone capture；local VAD 只決定是否送 ASR，不可令原 audio 消失。
- 約 15 秒一段：寫 `.partial.wav` → close/finalize header → atomic rename 成 `.wav` → 寫 manifest。
- Capture callback 只搬 bytes，不 await HTTP/DB/ASR/LLM。
- 重啟掃描 spool，從未收到 `STORED` 的 sequence 繼續上傳。

**通過證據**

- 連續錄 3 小時；檔案 sequence 無洞、WAV 可逐段解碼。
- 關 UI、重載、讓 AI Server 離線 10 分鐘，capture 不停且 pending 可見。
- Process restart 後 queue 恢復；partial file 被隔離，不冒充完整 chunk。

### Gate 2 · SenseVoice adapter throughput

**做甚麼**

- 以穩定 WAV contract 呼叫公司 endpoint；把 provider response 封在 adapter。
- timeout、bounded retry、backoff、error classification；從 disk retry，不從 memory 猜回 audio。
- 保存 raw provider response/latency metadata，但 log 不記完整敏感 transcript。
- 清理 SenseVoice control tags 只影響 display text，raw 結果仍可追查。

**通過證據**

- 粵語、普通話、英文與 code-switch 小樣本人工核對。
- p95 chunk-to-transcript latency 有量測；持續處理率高於 audio 產生率，backlog 不長期上升。
- 429/5xx/timeout 後沒有 duplicate transcript，也沒有丟 WAV。

### Gate 3 · AI Server durable intake + PostgreSQL

**做甚麼**

- `PUT chunk` 以 `(sessionId, sourceId, sequence)` idempotent。
- File + metadata + durable transcription work 必須有可恢復的一致邊界；真正持久後才回 `STORED`。
- Transcript 有 sequence/revision；不同來源不可用 arrival time 假裝成全域順序。
- Snapshot endpoint 一次重建 session/capture/transcript/note/material/chat。

**通過證據**

- 同一 chunk 重送十次只產生一份 work/result。
- 在寫入每個階段 kill server，重開後不是恢復就是清楚 quarantine，不能悄悄遺失。
- PostgreSQL unavailable 不會停止 Audio Client 本機錄音。

Module 06 的 Compose/schema 只用來學概念；這一 gate 才另寫正式 migrations、repository integration tests 與 backup/retention flow。

### Gate 4 · Live transcript UI + reconnect

**做甚麼**

- UI 首次載入與 reconnect 以 snapshot + version/cursor 作 correctness baseline。
- SSE/WebSocket 只作「有更新」提示；斷線 fallback polling。
- 顯示 recorder 真實狀態、pending uploads、ASR backlog、最後成功時間與 error。
- Transcript/LLM/search output 用 text rendering，不能直接進 HTML parser。

**通過證據**

- 錄音中關 tab 5 分鐘再開，UI 完整追上且 recorder 未停止。
- SSE 重複、遺失、亂序不會產生重複段落。
- 320/768/1024/1440 px、keyboard、screen reader live status 可用。

### Gate 5 · Rolling note with Microsoft Agent Framework

**做甚麼**

- 用 Microsoft Agent Framework (`Microsoft.Agents.AI`)，不是 Semantic Kernel；先以一個 agent + 明確 prompt/schema，不做 multi-agent orchestration。
- Gemini provider/model 由設定注入；實作當日再核對可用 Flash/Flash-Lite model ID、quota、region 與 data terms。
- 每 5 個 active minutes 或 transcript threshold 產生新 revision；只讀取上一版 note + 新的 contiguous transcript + relevant material。
- 每版存 `throughSequence`、model、prompt/version、timestamp；不能每 5 分鐘重送全 3 小時內容。
- 對 prompt size、output tokens、timeout、concurrency 設上限。

Microsoft 官方說明任何提供 `IChatClient` 的 provider 都可作 Agent Framework 基礎；Gemini integration 有獨立指引。[Microsoft Agent Framework providers](https://learn.microsoft.com/en-us/agent-framework/agents/providers/) · [Google Gemini integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)

**重要現況：**官方 Gemini capability table 對 C# provider-hosted Web Search 標為不支援，所以 search 應先是 AI Server 自己控制的 allowlisted adapter，不可假定 Gemini .NET agent 自帶 web grounding。Microsoft integration 頁的 install command 仍帶 `--prerelease`，但本次已實際 compile NuGet stable `Microsoft.Agents.AI 1.20.0`；日後仍須 pin 確實測過的版本並讀 release notes。[Microsoft: self-host Agent Framework](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting)

**通過證據**

- 每版只涵蓋連續 transcript；中間 sequence 未完成時 watermark 不越過洞。
- 用 lecturer-provided reference answer 檢查 omission、hallucination、term consistency。
- 記錄每次 latency/input-output tokens/error/cost estimate；舊 note 在 Gemini failure 時仍可讀。

### Gate 6 · Live chat + controlled search

**做甚麼**

- 問題綁定 question-time snapshot/version，答案標示使用到哪個 transcript watermark。
- 先以 Wikipedia 之類單一 allowlisted provider 做 search spike；server 產生 search query，限制數量/timeout/host。
- Citation 保存 title、canonical HTTPS URL、snippet、provider；UI 對 URL 再 allowlist。
- Lecture material、web result、transcript 都視為 untrusted context；agent tool 權限只讀且有上限。

Agent Framework 本身不會替 app sanitise message/model output；Microsoft API reference 明確要求開發者把 user/tool/model data 視為跨 trust boundary 的不可信資料。[Microsoft `AIAgent` security remarks](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.aiagent?view=agent-framework-dotnet-latest)

**通過證據**

- Search off 時不產生 external search request。
- Search failure 會標示「無線上來源」，不捏造 citation。
- 含 prompt injection 的 lecture material 不可令 agent 讀 secret、任意 URL 或執行 command。

### Gate 7 · 三小時 soak + fault injection

至少收集：

| 指標 | POC 建議 pass gate（先作假設，測後修訂） |
|---|---|
| Audio continuity | 完整 chunk duration 對 wall-clock 差距 ≤ 1%，每個 gap 有明確原因 |
| Durability | 所有 finalized chunk 最終收到一個 durable receipt；0 無法解釋的遺失 |
| Idempotency | retry 後 0 duplicate logical chunk/transcript revision |
| Throughput | 30 分鐘 steady state 後 ASR backlog 不持續上升 |
| Live latency | p95 finalized-chunk → visible transcript ≤ 30 秒 |
| Note latency | p95 scheduled revision → stored revision ≤ 60 秒 |
| Memory | 後兩小時無持續線性增長；用 profiling 解釋 plateau |
| Recovery | UI/server/DB/ASR 短暫故障後可自動或明確手動恢復 |
| Cost | 記錄 3 小時總 ASR/LLM/search calls、tokens 與估算費用 |

這些數字是 POC 的**初始驗證門檻**，不是已量測成績或 SLA。

Fault drill 至少包括：UI reload、AI Server kill/restart、PostgreSQL stop/start、SenseVoice timeout/5xx、Gemini missing key/quota、network offline、laptop sleep、disk low、同一 chunk 重送、最後 partial chunk。

### Gate 8 · Mobile decision

完成 Windows soak 後才選：

- **UI-only（推薦首版）**：responsive Web UI，手機只查看／問問題。
- **Native capture**：若靠近 lecturer 的收音價值足夠，再做 iOS/Android local chunk spool + retry；各平台另設 60–180 分鐘 background/audio test。
- **Browser/PWA capture**：只可作短期便利 demo 或 secondary fast path，不接受為唯一 durable recorder。

## Go / No-go checkpoint

完成 Gate 7 才決定 MVP：

### Go

- Audio durability 與 idempotency 過關。
- SenseVoice throughput 能追上 lecture，或 batch catch-up 時間可接受。
- Rolling note 在真實課堂材料上有可量化幫助，且 cost 可承受。
- 學生清楚知道 recording/third-party data flow，lecturer consent 可操作。

### No-go / redesign

- 任一 page/process/API outage 會不可恢復地丟 audio。
- ASR backlog 在正常網絡仍線性增長。
- Note hallucination 無法用 transcript citations/watermark 降到可接受。
- 三小時 battery/disk/thermal 或第三方 cost 不可接受。
- 校方／lecturer recording policy 不容許這個 data flow。

## 建議的第一個 implementation slice

當你準備由 spike 進入 POC code，只做一條最薄 vertical slice：

> Simulated Audio Client 產生 3 個有 sequence 的假 WAV → AI Server durable intake → explicit Demo ASR → PostgreSQL transcript → snapshot API → UI 顯示；重送 chunk #2 證明不重複。

這條 slice 不需要 Gemini、search、mobile 或真 microphone。它先證明 contract、durability、ordering、reconnect；成功後才把每個 fake adapter 逐一換成 Real。

## 用 study script 檢查學習 modules

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\07-poc-roadmap\study.ps1
```

它只讀取 `scripts/00-*` 至 `07-*` inventory，檢查每個 module 是否有 `README.zh-HK.md`、`study.ps1`、`index.html`，然後列出下一步；不會 build MVP、啟動服務或刪任何資料。加 `-Strict` 可讓缺檔時回傳 failure，適合完成所有 modules 後自驗。

## 官方參考

- [Microsoft Agent Framework documentation](https://learn.microsoft.com/en-us/agent-framework/)
- [Microsoft Agent Framework: Google Gemini integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)
- [Microsoft Agent Framework: self-hosting](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting)
- [Microsoft `AIAgent` API security remarks](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.aiagent?view=agent-framework-dotnet-latest)
- [MDN: `getUserMedia()`](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getUserMedia)
- [MDN: Page Visibility API](https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API)
- [MDN: MediaStreamTrack `ended`](https://developer.mozilla.org/en-US/docs/Web/API/MediaStreamTrack/ended_event)
