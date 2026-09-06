// 自己動手：幫個 record 改返啱名（配合 scripts/csharp-concept-02-naming.html 第 6 節）
//
// 目標：以下呢個 record 定義同用法用咗一堆唔跟 C# 命名慣例嘅名，
// 跟返 scripts/csharp-concept-02-naming.html 教嘅規則，逐個改做啱嘅名：
//   - lc（型別名）  -> LectureChunk
//   - id            -> SessionId
//   - seq           -> Sequence
//   - txt           -> Text
//   - dur           -> DurationSeconds
// 保持成個型別係 sealed record，並用改咗名之後嘅 property 印返同一段資訊。

// 原本（唔啱命名）嘅版本，留喺呢度做參考，唔好直接跑：
// record lc(Guid id, long seq, string txt, double dur);
//
// var x = new lc(Guid.NewGuid(), 0, "CAP theorem", 14.8);
// Console.WriteLine($"{x.seq}: {x.txt}");

// TODO 1：喺呢度定義改好名之後嘅 sealed record（型別名同四個 property 都要改）

// TODO 2：喺呢度建立一個 instance，並用改咗名之後嘅 property 印出
//         "0: CAP theorem"（sequence 同 text，格式同原本一樣）
