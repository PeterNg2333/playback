// 自己動手：一個攞唔到就用預設值嘅泛型 lookup helper（配合 scripts/csharp-concept-04-generics.html 第 7 節）
//
// 目標：寫一個 static TValue GetOrDefault<TKey, TValue>(Dictionary<TKey, TValue> lookup, TKey key, TValue fallback) method——
//   - 用 lookup.TryGetValue(key, out var value) 嘗試攞值
//   - 攞唔到就回傳 fallback，唔好拋 KeyNotFoundException
//
// 試用喺兩種完全唔同嘅 Dictionary，證明同一個 method 對唔同 key/value 型別組合都用得：
//   - Dictionary<Guid, LectureChunk>：搵一個唔存在嘅 session id
//   - Dictionary<string, double>：搵一個唔存在嘅語言代碼嘅預設 chunk interval

// TODO 1：用 Dictionary<Guid, LectureChunk> 試 GetOrDefault（一個 id 存在，一個唔存在）
// var bySession = new Dictionary<Guid, LectureChunk>();
// var knownId = Guid.NewGuid();
// bySession[knownId] = new LectureChunk(knownId, 0, "CAP theorem intro", 14.8);
//
// var missingId = Guid.NewGuid();
// var placeholder = new LectureChunk(missingId, -1, "(chunk not received yet)", 0);
//
// Console.WriteLine(GetOrDefault(bySession, knownId, placeholder).Text);
// Console.WriteLine(GetOrDefault(bySession, missingId, placeholder).Text);

// TODO 2：用 Dictionary<string, double> 試 GetOrDefault（一個 key 存在，一個唔存在）
// var chunkIntervalByLanguage = new Dictionary<string, double> { ["yue"] = 12.0 };
// Console.WriteLine(GetOrDefault(chunkIntervalByLanguage, "yue", 15.0));
// Console.WriteLine(GetOrDefault(chunkIntervalByLanguage, "en", 15.0));

// TODO 3：喺呢度寫 GetOrDefault<TKey, TValue> 呢個 method
// static TValue GetOrDefault<TKey, TValue>(Dictionary<TKey, TValue> lookup, TKey key, TValue fallback) => ...

// TODO 4：喺呢度定義 LectureChunk（同《C# 中階銜接》第 2 節一樣）
// public sealed record LectureChunk(Guid SessionId, long Sequence, string Text, double DurationSeconds);
