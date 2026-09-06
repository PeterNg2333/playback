// 自己動手：平均錄音長度（配合 scripts/csharp-concept-03-methods.html 第 9 節）
//
// 目標：寫一個 static double AverageDuration(params double[] seconds) method——
//   - 陣列冇任何元素（空陣列）就回傳 0，唔好除以零
//   - 否則回傳所有數值嘅平均數

// TODO 1：喺下面分別試 AverageDuration(14.8, 15.0, 4.2) 同 AverageDuration()，並印出結果
// Console.WriteLine(AverageDuration(14.8, 15.0, 4.2));
// Console.WriteLine(AverageDuration());

// TODO 2：喺呢度寫 AverageDuration 呢個 method
// static double AverageDuration(params double[] seconds) => ...
