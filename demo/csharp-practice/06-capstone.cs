// 動手做：迷你 rolling notes pipeline
// 目標：讀入幾個 LectureChunk，清理文字、排序、轉做筆記行，
// 再用 async 方法模擬送去 ASR 正規化，最後用 try/catch 處理一個壞輸入。
//
// 你已經喺 01~05 分別寫過以下每一步，依家將佢哋組合埋一齊：
//   01-record.cs         -> record + with
//   02-list-linq.cs       -> List<T> + LINQ
//   03-nullable.cs         -> nullable + ??
//   04-async.cs             -> async/await
//   05-error-handling.cs     -> try/catch
//
// 跟住 TODO 順序填，填完一個就 dotnet run 06-capstone.cs 睇下有冇報錯。
// 唔識點寫可以睇 scripts/csharp-dotnet-tutorial-2.html 第 7 節嘅完整答案。

// TODO 1：喺檔案最尾（PASS 嗰行之後）貼上 LectureChunk record 定義
//         （參考 01-record.cs）。C# 規定 top-level statements 要行先，
//         type 定義（record/class）要行後，所以 record 一定要擺落面。


// TODO 2：整一個 sessionId（Guid.NewGuid()）同一個 List<LectureChunk>，
//         最少 4 筆，其中一筆 Text 要係空白／全 space，用嚟驗證下面會過濾佢


// TODO 3：用 LINQ 做 Where -> OrderBy -> Select -> ToList，
//         產生 noteLines（格式："{sequence + 1}. {text}"）
//         然後 foreach 印出嚟，睇下空白嗰筆有冇消失


// TODO 4：宣告 string? lecturerName = null，
//         用 ?. 同 ?? 計出 lecturerLabel 並印出嚟


// TODO 5：寫一個 static async Task<string> NormalizeTranscriptAsync(string text)，
//         入面 await Task.Delay(...) 模擬等待 ASR，
//         然後 return text.Trim().ToUpperInvariant()；
//         喺主程式 await 呢個方法處理 noteLines 入面第一行，並印出結果


// TODO 6：寫一個會 throw ArgumentOutOfRangeException 嘅呼叫
//         （可以照抄 05-error-handling.cs 嘅 ParsePositiveInterval），
//         用 try/catch 包住，catch 到就印訊息，唔好畀程式 crash


Console.WriteLine("PASS: capstone completed.");
