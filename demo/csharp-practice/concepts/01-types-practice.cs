// 自己動手：安全解析錄音長度（配合 scripts/csharp-concept-01-types.html 第 8 節）
//
// 目標：寫一個 static double? ParseDurationSeconds(string input) method——
//   - 用 double.TryParse 嘗試將 input 轉做 double
//   - 轉唔到，或者轉到但係負數，回傳 null
//   - 轉到而且係非負數，就回傳嗰個值

// TODO 1：喺下面分別試 "14.8"、"-3"、"abc" 三個輸入，並印出結果
// Console.WriteLine(ParseDurationSeconds("14.8"));
// Console.WriteLine(ParseDurationSeconds("-3"));
// Console.WriteLine(ParseDurationSeconds("abc"));

// TODO 2：喺呢度寫 ParseDurationSeconds 呢個 method
// static double? ParseDurationSeconds(string input) => ...
