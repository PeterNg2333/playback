# ASR adapters 與 Qwen3 研究（2026-09-28）

## 官方接口與選擇

- [OpenRouter Qwen3 ASR 1.7B](https://openrouter.ai/qwen/qwen3-asr-1.7b)：model ID 是 `qwen/qwen3-asr-1.7b`。
- [OpenRouter STT 文件](https://openrouter.ai/docs/guides/overview/multimodal/stt)：`POST https://openrouter.ai/api/v1/audio/transcriptions`，Bearer key；本實作使用 JSON 的 `model`、`input_audio.data`（base64 WAV）、`input_audio.format`、`response_format: json`，讀取 `text` 和可選的 `usage.seconds`。沒有強制語言提示，以免混合粵語／英文被誤限制。其他轉錄模型可使用同一 adapter 和不同 model ID；須先確認該模型支援 STT。
- [Qwen 官方 repo](https://github.com/QwenLM/Qwen3-ASR)：1.7B 和 0.6B 支援粵語；真正 incremental audio streaming 的官方推理路徑使用 vLLM。官方 streaming 範例不提供 timestamps；不能把其能力直接套到 OpenRouter REST 接口。
- OpenRouter STT 文件描述整段音訊上傳和 JSON 回應，沒有在此接口列出 realtime PCM／WebSocket 協議。收到 HTTP response 的串流或 model card 的「streaming」字樣，不等於目前 Playback 已能邊收音邊產生 partial transcript。

1.7B 是否比公司 SenseVoice 快，尚未 live 量度。官方模型吞吐量並不包括本機封存、網絡上傳、服務排隊和本機保存。現在每約 30 秒封存 WAV，然後最多兩個並行 ASR；UI 每段的等待包括封存時間。換 REST 模型不會消除這個等待。

## 配置與實際路徑

憑證只由 process environment 讀取；正常 `pnpm.cmd dev` 會把可選根目錄 `.env` 載入 process environment，既有環境變數優先，不顯示 key。

| 設定 | 行為 |
| --- | --- |
| `PLAYBACK_ASR_PROVIDER=openrouter` | 明確使用 OpenRouter；key 缺失時失敗，不偷偷切回其他 provider |
| `PLAYBACK_ASR_PROVIDER=sensevoice` | 保留公司 SenseVoice |
| 未指定 provider | 有非空且非範例值的 `OPENROUTER_API_KEY` 時用 OpenRouter；否則保留 SenseVoice |
| `PLAYBACK_ASR_MODEL` | OpenRouter model ID，預設 `qwen/qwen3-asr-1.7b`；SenseVoice 不使用此設定 |
| `PLAYBACK_PAUSE_EXTERNAL_ASR=yes` | 暫停 ASR queue，仍保存音訊 |
| `PLAYBACK_OFFLINE_TEST=yes` | 所有外部 ASR adapter 呼叫拒絕執行 |

改配置後先停止錄音，再於原終端 Ctrl+C、執行 `pnpm.cmd dev` 和重載網頁。Transcript settings 顯示實際 provider、model 和 transport；`/api/health` 亦回報 `asr`。已完成逐字稿保持原文；新逐字稿保存 `AsrProvider`／`AsrModel`，舊紀錄不猜測模型。待處理及手動 retry 的 chunk 使用目前配置。

`AsrProcessor` 依賴 `IAsrAdapter`，不用認識 SenseVoice 或 OpenRouter HTTP schema。`AsrRequest` 保留 session、source、sequence、hash。OpenRouter 發送穩定的 `Idempotency-Key`；沒有聲稱 provider 一定按此去重。本機仍以 chunk ID 保存唯一完成逐字稿，queue 避免同一 chunk 同時排入。

兩個 REST adapter 共用 16 kHz mono PCM WAV 正規化，原始錄音保留。OpenRouter 固定 HTTPS endpoint、禁 redirect、25 MB 本機輸入限制、512 KB 回應限制、涵蓋回應讀取的 70 秒 deadline；SenseVoice 保留 allowlist endpoint 與 120 秒 deadline。HTTP failure 只顯示 provider/status，不回顯 provider body 或 key。

## Streaming 擴充邊界

`IStreamingAsrAdapter` 和 `IAsrStream` 定義獨立來源的串流生命週期：Open、按 sequence Send 16 kHz mono PCM16、ReadUpdates（時間與 IsFinal）、Complete、Dispose。它是新增 realtime provider 的合約，**目前沒有 provider 實作或 capture 接線**；現在運行的兩個 adapter 都是 REST。

實作 realtime adapter 時，還需要按 source 開 stream、把 capture callback 的 PCM 正規化後送入有上限的 queue、在暫停／停止時 flush，並將 final results 與保存的 chunk 範圍對齊。Partial results 只作暫時 UI 顯示，不寫成完成逐字稿或觸發筆記；斷線後用保存的 chunk 走 `Transcribe` 恢復。新增 provider 必須按其官方 realtime 協議驗證，不以 REST polling 假裝串流。

## 可重現檢查

離線 protocol checks 使用 in-memory HTTP handler 驗證 OpenRouter payload、WAV 格式、穩定 retry identity、空稿、未知 schema、401、回應大小限制、取消、缺 key、offline guard。沒有音訊上傳。

```powershell
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/
```

比较工具預設只在本機解碼 repo Week 3 sample 的頭兩段 30 秒音訊，不呼叫 provider、不改資料庫：

```powershell
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/ -- --asr-compare
```

**以下是明確 opt-in 的 live 指令**：把同一兩段音訊送去 SenseVoice 和 OpenRouter 各兩輪，合共 8 次請求，可能收費。先將真實 `OPENROUTER_API_KEY` 載入 process environment；不要保留 `PLAYBACK_OFFLINE_TEST=yes`。此直接 .NET 指令不自行讀 `.env`。結果只顯示每次端到端時間、延遲／音訊秒數、文字長度及 provider 回報的推理時間（缺失會顯示 unreported），不輸出課堂文字。第二輪反轉 provider 次序；樣本小，不能推斷三小時吞吐量或文字準確度。

```powershell
dotnet run --project app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false -p:OutputPath=bin/verification/net10.0/ -- --asr-compare --live
```

本次只執行離線驗證。未量度 Qwen live latency、粵語／英文準確度、三小時吞吐量或真正串流恢復。
