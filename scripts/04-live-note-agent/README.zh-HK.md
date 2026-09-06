# Module 04 — Rolling LLM note agent

## 這個 spike 要證明甚麼？

把三小時 lecture 的完整 transcript 每次重送給 LLM 並不是好起點。這個 module 先驗證較小的單位：每個 ASR chunk 有遞增 ID，note revision 保存 `processedThrough` watermark；重試同一 chunk 時不會重複內容。若先收到 3、後收到 2，demo 會拒絕 gap，而不是把 watermark 跳到 3 並永久遺漏 2。真正呼叫 Gemini 是獨立、明確的 opt-in。

```powershell
# 完全離線，先看 watermark / idempotency
& .\scripts\04-live-note-agent\study.ps1
& .\scripts\04-live-note-agent\study.ps1 -SelfTest

# 只看 package/model/config，不讀 key value、不連網
& .\scripts\04-live-note-agent\study.ps1 -Mode Inspect

# 明確呼叫 Gemini
$env:GOOGLE_AI_STUDIO_API_KEY = '你的 key'
& .\scripts\04-live-note-agent\study.ps1 -Mode Gemini
```

`Gemini` mode 使用 .NET 10 file-based app 內的兩個 pinned package：`Google.GenAI@1.21.0` 及 `Microsoft.Agents.AI@1.20.0`。它採 Microsoft 官方 integration 的形狀：

```csharp
ChatClientAgent agent = new(
    new Client(vertexAI: false, apiKey: apiKey).AsIChatClient(model),
    name: "RollingLectureNoteEditor",
    instructions: instructions);
```

這不是 Semantic Kernel。Agent Framework 管理 agent definition/orchestration；Google client 管 Gemini authentication 和 provider request。

這個 network probe 另設 90 秒 cancellation timeout、2,000 output-token 上限與 250 KB/檔 input cap，避免一個學習 command 無界消耗；它仍可能產生 Gemini API 費用。

## 為何預設 Flash-Lite？

`gemini-3.5-flash-lite` 是穩定、低 latency/低成本的高 throughput model，適合頻密的小修訂。`gemini-3.8-flash` 可用作較少但較難的 consolidation pass。這只是待量度的 hypothesis：應用同一批 lecture chunks 比較 latency、費用、遺漏率與 hallucination，再決定 routing。

## 日後實作的逐步路線

1. ASR 成功後先永久保存 immutable transcript chunk，再排 note job。
2. Job 只讀 watermark 後**連續**的 chunks，以及上一版 note；有 gap 就 buffer/retry，同一 lecture 同時只准一個 editor。多收音來源時，watermark 必須包含 `sourceId`，不能只用一個全域 sequence。
3. Prompt 附上相關 lecture material excerpt，而不是無限制放整份教材。
4. 要求 structured result：`markdown`、`processedThrough`、`uncertainties[]`、`sourceChunkIds[]`。
5. 在 code 驗證 watermark 沒有倒退、source IDs 存在、output size 合理；LLM output 一律視作不可信資料。
6. 用 transaction 同時寫 `note_revision` 及新 watermark。Crash 前後重跑都不重複。
7. UI 以 SSE/WebSocket 收到「新 revision available」，但 recorder 即使 UI reload 仍繼續。
8. 每 2–5 分鐘做小 revision；章節完結或下課後才做較大的 consolidation。間隔必須以實測決定。

## Lecture material 的安全界線

教材和 transcript 都可能包含 prompt injection 式句子；它們只是 quoted data。此 spike 不給 agent tools，所以最壞結果主要是錯誤 draft。正式系統仍須限制 prompt 大小、token/cost、保存 revision history，並在 UI 明示「AI draft」。不要把 API key、其他課堂資料或未獲同意的個人資料送進 prompt。

## Source

- [Microsoft Agent Framework — Google Gemini](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini)
- [Google.GenAI NuGet 1.21.0](https://www.nuget.org/packages/Google.GenAI/1.21.0)
- [Microsoft.Agents.AI NuGet 1.20.0](https://www.nuget.org/packages/Microsoft.Agents.AI/1.20.0)
- [Gemini model list](https://ai.google.dev/gemini-api/docs/models)
- [.NET file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps)
