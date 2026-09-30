# ASR adapters 與 Qwen3 研究（2026-09-28）

## 官方接口與選擇

- [OpenRouter Qwen3 ASR 1.7B](https://openrouter.ai/qwen/qwen3-asr-1.7b)：model ID 是 `qwen/qwen3-asr-1.7b`。
- [OpenRouter STT 文件](https://openrouter.ai/docs/guides/overview/multimodal/stt)：`POST https://openrouter.ai/api/v1/audio/transcriptions`，Bearer key；本實作使用 JSON 的 `model`、`input_audio.data`（base64 WAV）、`input_audio.format`、`response_format: json`，讀取 `text` 和可選的 `usage.seconds`。Auto 不送語言提示；session 可明確指定語言，詳見下方。其他轉錄模型可使用同一 adapter 和不同 model ID；須先確認該模型支援 STT 和所選語言。
- [Qwen 官方 repo](https://github.com/QwenLM/Qwen3-ASR)：1.7B 和 0.6B 支援粵語；真正 incremental audio streaming 的官方推理路徑使用 vLLM。官方 streaming 範例不提供 timestamps；不能把其能力直接套到 OpenRouter REST 接口。
- OpenRouter STT 文件描述整段音訊上傳和 JSON 回應，沒有在此接口列出 realtime PCM／WebSocket 協議。收到 HTTP response 的串流或 model card 的「streaming」字樣，不等於目前 Playback 已能邊收音邊產生 partial transcript。

2026-09-28 已比較 Qwen、Whisper V3/Turbo 的 21 個短音訊請求，結果見本機 validation 索引；未以同一自然語音比較公司 SenseVoice。現在錄音最多約 8 秒封存 WAV；滿 3 秒且所有來源至少 700 ms 沒有語音時會提早封存。正式 ASR 最多兩個並行。模型本身仍可能很慢，不能保證 2 秒內出字。

## 廣東話與英文、即時灰字

Transcript settings 可按 session 選擇 `qwen/qwen3-asr-1.7b`、`openai/whisper-large-v3` 或 `openai/whisper-large-v3-turbo`，或沿用環境的預設模型。選擇保存於 `AsrModel`，之後的請求及 retry 使用此模型，逐字稿保存實際呼叫的模型 ID。SenseVoice 沒有 OpenRouter 模型選單。

[OpenRouter 官方列出這兩個 Whisper 模型](https://openrouter.ai/blog/tutorials/transcription-on-openrouter/)；[Whisper V3 有廣東話專用 token](https://github.com/openai/whisper/discussions/1762)。[Turbo 是 V3 的速度優化版本](https://github.com/openai/whisper)。可先試 Turbo 的延遲，再與 V3 比較廣東話及中英夾雜準確度；沒有宣稱任何一款在你的錄音一定勝過 Qwen。

`LiveAsrSession` 接收 device callback 真正錄到的音訊，正規化為連續 16 kHz mono PCM16。REST provider 約每 2 秒嘗試轉錄目前分段的累積音訊；每來源最多一個預覽請求、15 秒 deadline，慢請求期間跳過新預覽，不排隊。正式 ASR 有待處理積壓時暫停預覽。回傳文字用灰字顯示，封存後可保留預覽，正式逐字稿完成後替換；錯誤及空結果不偽造文字。停止會取消預覽並封存剩下的正式 WAV。

REST 預覽會重複上傳部分音訊，增加費用；8 秒持續語音可有 2／4／6 秒預覽及 8 秒正式請求，合計約為只送正式稿的 2.5 倍音訊。實際取決於停頓、延遲及積壓。短分段較少上下文，可能影響準確度，實機試音仍必要。

若 adapter 實作 `IStreamingAsrAdapter`，capture 每來源開 stream，直接送有上限的 PCM queue、讀取並替換 partial hypotheses，停止時 flush／dispose，不做 REST 預覽。斷線或 queue 過載會退回短音檔預覽。健康串流的語言／模型在開 stream 時固定，UI 要求停止錄音才更改。**目前兩個具體 adapter 都是 REST；OpenRouter 沒有此處已確認的 realtime ASR 協議，不能宣稱已原生 streaming。**

中間結果只存在記憶體，不寫入完成逐字稿或觸發筆記、翻譯、問答；正式 WAV 仍走 `AsrProcessor`，作為一致的完成與恢復邊界。即使 provider 把串流 utterance 標記 final，也要等保存的 WAV 正式轉錄才成為可引用來源。

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

`IStreamingAsrAdapter` 和 `IAsrStream` 定義獨立來源的串流生命週期：Open、按 sequence Send 16 kHz mono PCM16、ReadUpdates（時間與 IsFinal）、Complete、Dispose。Capture 接線已具備，尚未有具體 realtime provider adapter。Update 時間以 stream 第一個 PCM sample 為零點，相同 StartMs 替換上一次假設。

新增 realtime adapter 必須按 provider 官方協議實作及驗證，支援 cancellation，並檢查 chunk／utterance 的實際時間對齊。短請求預覽在 UI 標明 short audio requests，不以 REST polling 假裝原生串流。

## 語言設定

Transcript settings 保存三組獨立的 session 設定，不使用字形設定猜測錄音語言：

| 設定 | 選项／API 值 | 何時生效 |
| --- | --- | --- |
| ASR spoken language | Auto `auto`、廣東話 + English `yue-en`、廣東話 `yue`、普通話 `zh`、英文 `en` | 之後的 ASR 請求；已完成逐字稿不重新辨識 |
| Notes output language | TC `zh-Hant`、SC `zh-Hans`、EN `en` | 下一次 AI revision，包括舊筆記的語言改寫；舊版本保留 |
| Translation target language | 廣東話繁體 `yue-Hant`、標準書面中文 TC `zh-Hant`／SC `zh-Hans`、EN `en`、日／韓 `ja`／`ko` | 啟用翻譯時處理既有／新逐字稿；改目標會重新排隊 |

`PUT /api/sessions/{id}/languages` 保存 `asrLanguage`／`noteLanguage`／`asrModel`（null 沿用環境預設）；翻譯沿用 `/translation`。新舊 session 的預設為 ASR Auto、Notes TC、Translation TC。舊資料缺少欄位時按預設讀取，不遷移或清空資料庫。API health 的 `sessionLanguageSettings` 防止舊 API 靜默忽略新設定。

OpenRouter adapter 在 Auto 及「廣東話 + English」時完全省略 `language`，避免強制單一語言；混合模式仍把中文字顯示為繁體。其餘值按原樣送到 STT 的 top-level `language`。`yue` 是 [Qwen hosted ASR 官方文件](https://help.aliyun.com/en/model-studio/qwen-asr-api-reference) 的廣東話代碼，與普通話 `zh` 分開。OpenRouter 的統一文件把此參數描述為 ISO-639-1，未逐一列出 provider 的粵語代碼轉送；**目前只驗證本機送出的 yue/zh/en payload，未 live 驗證該路由會否接受或採用 yue**。不支持的請求會顯示真實 provider 錯誤，不會偷偷去掉提示重試成成功。

[Qwen3 官方 toolkit](https://github.com/QwenLM/Qwen3-ASR) 可指定語言以固定辨識語言。據此，正確提示預期能減少語言判斷錯誤，但不代表已量度 Qwen 1.7B 在 OpenRouter 的準確度提升或速度提升。Alibaba 的 hosted Flash 文件建議已知單一語言可指定以改善辨識，未知／混合語言則不指定；這不是本機 1.7B 路由的實測。粵語夾英文課堂可先用 Auto，比較後再決定是否固定 yue。

SenseVoice 公司 endpoint 的已驗證 contract 只有檔案上傳，沒有已確認的 language hint 欄位，所以 adapter 宣告 `supportsLanguageHint=false`，UI 明示自動辨識，不虛構可設定的辨識功能。session 的廣東話顯示偏好仍會用於本機字形轉換。

廣東話及「廣東話 + English」模式使用 [Windows LCMapStringEx 的 Traditional Chinese mapping](https://learn.microsoft.com/en-us/windows/win32/api/winnls/nf-winnls-lcmapstringex) 將新辨識稿的簡體字轉成繁體顯示；Auto 只有 provider 實際回報 Cantonese／yue 時才套用。英文與粵語詞保留。這是字形轉換，不能修正 ASR 誤聽，也不是 OpenCC 的詞組消歧。`Original` 保留 provider 原文，只有字形不同才保存 `DisplayOriginal`；引用、選取、筆記、翻譯和術語抽取使用顯示文字。舊逐字稿不因新設定而改写。此 converter 與現在錄音 prototype 一樣依賴 Windows。

新逐字稿保存真正送出的 `AsrLanguageHint` 和可選的 `AsrDetectedLanguage`，兩者不混淆；request retry identity 包含 language／model，避免換提示時重用其他設定的 provider request。筆記 hash 包含目標語言，AI 版本保存 `OutputLanguage`；語言在生成途中更改時拒絕保存過期結果，避免舊語言結果覆寫新設定。

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

本次只執行離線驗證，包含 mock streaming 的提前回傳、修訂替換、PCM sequence／跨 callback 重採樣、停止 flush 及失敗 fallback；UI 灰字及正式稿替換、模型／語言保存亦有瀏覽器檢查。未量度 OpenRouter live latency、粵語／英文準確度、三小時吞吐量或真實 provider 串流恢復。
