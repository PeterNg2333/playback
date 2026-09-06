# 03 — SenseVoice HTTP Adapter Study

這個 spike 把公司的 SenseVoice 呼叫拆成可檢查的 request plan。預設只驗證 WAV 和顯示將會送出的 request，**不會連網**；只有明確加入 `-Send` 才會上傳一次。

## 已核實的 contract

在本機 reference `C:\Users\a1831\Documents\GitHub\ai-kiosk` 找到的實際 route，以及 2026-09-06 的 endpoint probe，均指向：

```text
POST https://dev-aks.setsailapi.com/stt/infer/upload
Content-Type: multipart/form-data; boundary=...

part name: file
part filename: audio.wav
part Content-Type: audio/wav
```

不要使用原先猜測的 `/sensevoice-inference`。2026-09-06 probe 用有效但無語音的 WAV 得到 HTTP 200；這只證明 transport contract 當時可用，不代表 latency、transcript 或 JSON schema 永遠不變。

## 1. 先做 dry-run

在 repository root 執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\scripts\03-sensevoice-adapter\study.ps1
```

沒有提供 `-WavPath` 時，script 會在本 module 的 `.artifacts/` 產生 250 ms 靜音 probe。然後它會：

1. 驗證檔案存在而且 extension 是 `.wav`。
2. 限制大小為 44 bytes 至 25 MiB。
3. 檢查 `RIFF`／`WAVE` magic bytes，而不是只相信副檔名。
4. 計算 SHA-256，方便比較重試是否仍是同一份 audio。
5. 寫出 `.artifacts/request-plan.json`。
6. 顯示 `DRY RUN` 並結束；這條 path 不會建立 `HttpClient`。

Request plan 亦會明示 `allowAutoRedirect: false`。真正送出時，script 使用
`HttpClientHandler.AllowAutoRedirect = $false`；如果 endpoint 回傳 3xx，audio 不會被自動重新 POST 到另一個 host，而該 response 會按非成功狀態處理。`HttpClient` 與 handler 由 `finally` 分別 dispose。

Self-test：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\scripts\03-sensevoice-adapter\test.ps1
```

## 2. 經同意後才送出

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\scripts\03-sensevoice-adapter\study.ps1 `
  -WavPath C:\path\chunk-000.wav `
  -TimeoutSeconds 120 `
  -Send
```

`-Send` 是刻意的 safety gate。Lecture audio 可能包含 lecturer、同學姓名、提問或其他個人資料；先確認有錄音及第三方處理的同意。

Response body 會原樣保存到 `.artifacts/last-response.body`，metadata 則在 `last-response-metadata.json`。Script **不會假設** response 一定有 `raw`、`text`、`language` 或任何其他 field。External response 是不可信資料；先觀察實際 JSON，才為它寫 parser 和 contract test。

## 為何 adapter 不應散落在業務 code？

最小 C# contract 可以是：

```csharp
public interface IAsrClient
{
    Task<AsrResult> TranscribeAsync(
        Stream pcm16Wav,
        CancellationToken cancellationToken);
}
```

業務 worker 只依賴 `IAsrClient`，不需要知道 multipart field 叫 `file`。`SenseVoiceAsrClient` 才負責：

- 固定／allowlist endpoint，避免 user-controlled URL 造成 SSRF；
- 關閉 automatic redirect，避免 provider 以 3xx 把 audio 轉送到另一個 host；
- 建立 `MultipartFormDataContent`；
- 把 `StreamContent.Headers.ContentType` 設為 `audio/wav`；
- 設定 120 秒 timeout 並傳遞 `CancellationToken`；
- 檢查 HTTP status、response 大小及 JSON shape；
- 把 provider-specific response 轉為自己的 `AsrResult`。

之後加入另一個 ASR，只需新增另一個 implementation。Demo mode 和 SenseVoice mode 要由 config 明確選擇，不能在 SenseVoice 失敗後靜默變成假 transcript。

## `study.ps1` 對應的 C# 概念

| PowerShell | C# / .NET |
|---|---|
| `HttpClient` | 由 `IHttpClientFactory` 建立／管理 client |
| `MultipartFormDataContent` | 同名 BCL class |
| `StreamContent` | 不必把整段 audio 載入 RAM |
| `try/finally` + `Dispose()` | `using` / `await using` |
| `TimeoutSeconds` | typed client timeout + per-request cancellation |
| raw response file | 先 capture evidence，再設計 response DTO |

## 3 小時 lecture 的重試策略

不要把三小時 audio 當成一次 HTTP request。以短 chunk 工作：

1. Audio client 先 durable-save WAV 和 manifest。
2. 每個 chunk 使用穩定的 `(sessionId, sourceId, sequence)`。
3. Server 以 SHA-256 防止同一 key 對應不同內容。
4. Timeout 屬於「結果未知」，不可因此刪除本機 WAV。
5. 收到 server 的 `STORED` acknowledgement 後，才視 retention policy 清理。
6. ASR 可以失敗及重試，而 capture callback 不被 HTTP latency 阻塞。

這個 endpoint 是同步 whole-file inference，不是 streaming contract；live feeling 來自短 chunk pipeline，而不是假設 provider 支援 WebSocket。

打開 [index.html](./index.html) 可直接用 `file://` 查看 request anatomy；頁面不載入任何外部 CSS、font 或 JavaScript。
