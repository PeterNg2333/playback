# 01 — 用一個檔案學 C# 基礎

這一課使用 .NET 10 **file-based app**：只有 `basics.cs`，沒有 solution、`.csproj` 或 NuGet package。它很適合 study script；到正式 POC 才把概念搬進 project folders。

Microsoft 的 file-based app 文件適用於 .NET 10 SDK 或以上：<https://learn.microsoft.com/dotnet/core/sdk/file-based-apps>

## 1. 執行完整例子

```powershell
.\scripts\01-csharp-basics\study.ps1
```

如果 execution policy 阻止 `.ps1`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\01-csharp-basics\study.ps1
```

想一邊看有行號的 source、一邊看 output：

```powershell
.\scripts\01-csharp-basics\study.ps1 -ShowSource
```

`study.ps1` 實際執行：

```powershell
dotnet run --file .\scripts\01-csharp-basics\basics.cs
```

刻意使用 `--file`。Microsoft 指出，如果目前目錄日後存在 project，沒有 `--file` 的 `dotnet run file.cs` 可能改為執行 project，並把檔名當參數；明示 `--file` 沒有這個歧義。

## 2. 預期看到甚麼

```text
[types] ... expected=720
[record] LectureChunk { ... }
[record] value-equality=True
[list+linq] input=4, useful=3
[nullable] lecturer=（未提供講師名稱）, slide=12
[async] normalized=CAP THEOREM INTRO
[error] handled=ArgumentOutOfRangeException, parameter=input
PASS: C# basics spike completed.
```

每個 `[marker]` 對應一個你之後會在 ASR + LLM notes 用到的語言概念。

## 3. Types：變數知道自己可以放甚麼

```csharp
string courseTitle = "Introduction to Distributed Systems";
int lectureMinutes = 180;
double chunkSeconds = 15.0;
bool isRecording = true;
var chunkCount = 720;
```

C# 是 strongly typed。`string`、`int`、`double`、`bool` 是明確 type；`var` 只是讓 compiler 從右邊推斷 type，並不是 JavaScript 式 dynamic。Microsoft 的 type system 導覽：<https://learn.microsoft.com/dotnet/csharp/fundamentals/types/>

應用到 POC：session title 是 `string`、chunk sequence 是 integer、錄音狀態是 `bool`。

## 4. Record：一筆不可隨意變形的 chunk data

```csharp
public sealed record LectureChunk(
    Guid SessionId,
    long Sequence,
    string Text,
    double DurationSeconds);

var cleaned = draft with { Text = draft.Text.Trim() };
```

`record` 適合以 data 為主的 type，compiler 會產生 value equality、可讀的 `ToString()`，以及 `with` copy。`with` 不修改原本 `draft`，而是產生一份改了 Text 的 copy。官方文件：<https://learn.microsoft.com/dotnet/csharp/fundamentals/types/records>

應用到 POC：audio manifest、transcript segment 和 note revision 都適合 immutable record；database entity 則不一定適合。

## 5. `List<T>`：同一 type 的可增長次序集合

```csharp
List<LectureChunk> chunks =
[
    cleaned,
    new(sessionId, 2, "Eventual consistency examples", 15.0)
];
```

`<LectureChunk>` 是 generic type argument，意思是這個 list 只接受 `LectureChunk`。與固定長度 array 不同，`List<T>` 可 Add／Remove。初學官方導覽：<https://learn.microsoft.com/dotnet/csharp/tour-of-csharp/tutorials/list-collection>

## 6. LINQ：把「過濾 → 排序 → 投影」讀成一條資料流程

```csharp
var noteLines = chunks
    .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text))
    .OrderBy(chunk => chunk.Sequence)
    .Select(chunk => $"{chunk.Sequence + 1}. {chunk.Text}")
    .ToList();
```

- `Where` 移除空 transcript。
- `OrderBy` 按 sequence 恢復 lecture 次序。
- `Select` 把 chunk 轉成顯示文字。
- `ToList` 在這一刻執行 query 並保留結果。
- `chunk => ...` 是 lambda：一個短小 function。

Microsoft LINQ 教學：<https://learn.microsoft.com/dotnet/csharp/linq/get-started/write-linq-queries>

應用到 POC：先處理連續 transcript frontier，再把正確順序的 segments 交給 note agent。

## 7. Nullable：把「可能沒有值」寫進 type intent

```csharp
string? lecturerName = null;
int? currentSlide = 12;
string label = lecturerName?.Trim() ?? "（未提供講師名稱）";
```

- `string?`：compiler 知道 reference 可能是 `null`。
- `int?`：value type 額外容許「沒有數字」。
- `?.`：只有 non-null 才存取 member。
- `??`：左邊為 null 時使用 fallback。

Nullable reference types 是 compile-time analysis，不會創造另一個 runtime `string` type。官方 null safety：<https://learn.microsoft.com/dotnet/csharp/fundamentals/null-safety/>

應用到 POC：未提供 lecturer、ASR 未辨識 language、尚未產生 note 都應明確 nullable，而不是用空字串扮「沒有值」。

## 8. `async` / `await`：等待 I/O 時不要霸住 thread

```csharp
static async Task<string> NormalizeTranscriptAsync(string text)
{
    await Task.Delay(25);
    return text.ToUpperInvariant();
}
```

`Task<string>` 表示「將來會完成並提供 string 的工作」。`await` 暫停這個 method 的後續流程，但不等於阻塞整條 thread，也不保證開一條新 thread。官方 async/await 說明：<https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/>

應用到 POC：HTTP upload、ASR endpoint、database 和 LLM 都是 async I/O；不要用 `.Result` 或 `.Wait()` 堵住 server thread。

## 9. Error handling：只 catch 你能處理的錯

```csharp
try
{
    _ = ParsePositiveInterval("-5");
}
catch (ArgumentOutOfRangeException exception)
{
    Console.WriteLine(exception.ParamName);
}
```

例子刻意只 catch 可理解的 `ArgumentOutOfRangeException`，而不是吞掉所有 `Exception`。正常的輸入分支先用 `int.TryParse`；真正的 invalid invariant 才 `throw`。Microsoft 建議能 recovery 才 catch，並優先使用 `using` 清理 disposable resource：<https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions>

應用到 POC：AI Server 暫停是可 retry 的 network failure；checksum mismatch 是資料完整性 failure；兩者不應被同一個空 catch 隱藏。

## 10. File 頂部的 `#:` directives

```csharp
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property TreatWarningsAsErrors=true
#:property PublishAot=false
```

- 固定 target 是 .NET 10。
- 開啟 nullable analysis。
- compiler warning 當 error，教材不帶 warning 通過。
- file-based app 的 publish 預設使用 Native AOT；本課不 publish executable，所以關掉 AOT，令 offline run 不需 ILCompiler/runtime packs。

同 folder 的 `NuGet.Config` 清空 package sources，因為本課沒有任何第三方 package。這令 sample 完全 offline，也避免不必要下載。將來加入 `#:package` 前，請移走／修改這個 lesson-local config，並重新做 dependency review。

## 11. 驗證

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\01-csharp-basics\verify.ps1
```

Verifier 會經 `study.ps1` 真正 compile/run `basics.cs`，檢查每個 lesson marker 和 `-ShowSource` 的 UTF-8 中文；亦會靜態檢查 HTML 的基本 accessibility／responsive 結構和外部 assets。它不是只檢查檔案存在。

## 練習（自己改完再跑）

1. 把 `lectureMinutes` 改成 90，估算 chunk 數目會變多少。
2. 加一個空白 chunk，確認 `useful` 不增加。
3. 把 `lecturerName` 設成你的名字，觀察 `??` fallback 不再使用。
4. 把 `"-5"` 改成 `"5"`，想想為何 `[error]` marker 會消失、verifier 會 fail。
5. 新增 `Language` property 到 `LectureChunk`；compiler 會指出所有需要補值的位置。
