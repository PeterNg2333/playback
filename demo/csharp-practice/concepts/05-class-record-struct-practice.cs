// 自己動手：由 mutable class 改做 immutable record（配合 scripts/csharp-concept-05-class-record-struct.html 第 8 節）
//
// 依家有一個設計錯咗嘅 mutable class，用 public field 裝住理應唔可以被人靜靜雞
// 改動嘅筆記草稿：
//
// public class LectureNoteDraft
// {
//     public string Title;
//     public string Body;
// }
//
// 目標：
//   - 將 LectureNoteDraft 改寫做一個 sealed record（positional 或 init-only 都得）
//   - 建立一個 draft instance
//   - 用 with-expression 產生一份改咗 Body 嘅新 copy edited
//   - 印出 draft、edited，同埋 draft == edited

// TODO 1：喺呢度用 with-expression 產生 edited，並印出 draft、edited、draft == edited
// var draft = new LectureNoteDraft("Week 1", "CAP theorem intro");
// var edited = draft with { Body = "..." };
// Console.WriteLine(draft);
// Console.WriteLine(edited);
// Console.WriteLine(draft == edited);

// TODO 2：喺呢度寫 LectureNoteDraft 呢個 sealed record
// public sealed record LectureNoteDraft(string Title, string Body);
