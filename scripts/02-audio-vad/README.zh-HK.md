# 02 — 本機 Audio Source of Truth 與 Energy VAD

這個 spike 用一個完全可重現的 4 秒 WAV，示範「先落盤、再切 chunk、再判斷有沒有聲音」的最小資料流。它不會開啟咪高峰，也不會連接網路。

## 先回答核心問題：錄音應該在 browser 還是 backend？

在這個產品中，錄音的 source of truth 應由登入 Windows 使用者 session 中的本機 desktop process 擁有；browser 只負責 UI、開始／停止指令與查看狀態。

Browser 的 `MediaStreamTrack` 屬於目前的 Document。重新整理、關 tab、browser crash 或換頁令 Document 消失時，錄音 track 也不能繼續。單頁應用程式的 client-side route 切換可以避免 reload，但這只是同一個 Document 仍然存在，不能抵抗關 tab 或 browser crash。Service Worker 也不能成為持續持有咪高峰的錄音 process。

因此建議的可靠次序是：

1. Desktop process 從咪高峰取得 sample。
2. 每個短 chunk 先寫成 `.partial`，完成 header 和 flush 後改名為 `.wav`。
3. 本機 WAV 成為可重試的 source of truth。
4. VAD 判斷 chunk 是否值得送 ASR。
5. 上傳成功並收到 durable acknowledgement 後，才按 retention policy 清理本機副本。

這樣 UI reload、AI server 暫停或網絡中斷都不會令已錄音內容立即消失。

## 執行

在 repository root 開 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\02-audio-vad\study.ps1
```

預期結果是四個 1 秒 chunk：

```text
0  SILENCE
1  SPEECH
2  SILENCE
3  SPEECH
```

所有輸出只會放在本 module 的 `.artifacts/`：

```text
.artifacts/
├── deterministic-lecture.wav
├── summary.json
└── chunks/
    ├── chunk-000.wav
    ├── chunk-001.wav
    ├── chunk-002.wav
    └── chunk-003.wav
```

用自己的檔案測試（只接受最多 25 MiB、16 kHz、mono、PCM16 WAV）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\02-audio-vad\study.ps1 `
  -InputPath C:\path\lecture.wav `
  -ChunkSeconds 5 `
  -VadThreshold 0.02
```

執行 deterministic self-test：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\02-audio-vad\test.ps1
```

## 逐步讀 `study.ps1`

1. `New-DeterministicLectureWav` 產生「靜音、440 Hz tone、靜音、tone」。固定輸入令測試不受房間噪音影響。
2. `Get-Pcm16WavInfo` 不相信副檔名；它檢查 `RIFF`、`WAVE`、`fmt `、`data`，以及 PCM16/mono/16 kHz 格式。
3. `Read-Pcm16Samples` 把每兩個 little-endian bytes 讀成一個 `Int16` sample。
4. 主迴圈按 `sampleRate × ChunkSeconds` 分段，並把每段重新寫成完整 WAV。
5. `Measure-NormalizedRms` 計算能量：`sqrt(mean(sample²)) / 32768`。
6. RMS 大於 threshold 就標成 `SPEECH`，否則是 `SILENCE`。

## 對應到之後的 C#/.NET 實作

| Study 概念 | POC 中的責任 |
|---|---|
| `short[]` samples | audio capture callback 收到的 PCM16 sample |
| 1–15 秒 WAV chunk | durable spool item |
| `.artifacts/chunks` | 未來 production local spool folder 的簡化示意 |
| RMS threshold | `IVoiceActivityDetector` 的第一個 adapter |
| `summary.json` | manifest：session/source/sequence/hash/timestamp |

Capture callback 應只做快速 copy／enqueue，不在 callback 內等待 database、HTTP、ASR 或 LLM。慢工作由有界 queue 的 worker 處理。

## 這個 VAD 能做甚麼、不能做甚麼

Energy VAD 適合證明 pipeline，但它不知道聲音是不是人聲。冷氣、拍枱、音樂也可能超過 threshold；遠距 lecturer 聲音則可能太小。MVP 可先用它避免傳送純靜音，之後才換 WebRTC VAD 或神經網絡 VAD。更換時保留同一個 `HasSpeech` adapter contract。

## 私隱與課堂使用

錄音前要取得 lecturer／參與者同意，UI 要清楚顯示錄音狀態。Audio、transcript 和 note 都可能含個人資料；預設 loopback-only、設定 retention，並讓使用者能刪除整個 session。

## 延伸閱讀

- [MDN: MediaStreamTrack](https://developer.mozilla.org/en-US/docs/Web/API/MediaStreamTrack)
- [MDN: Page Visibility API](https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API)
- [MDN: Service Worker API](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API)

打開 [index.html](./index.html) 可用視覺化方式溫習同一個資料流；它沒有外部資產，可直接用 `file://` 開啟。
