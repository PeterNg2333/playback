# C# practice files

配合 [scripts/csharp-dotnet-tutorial-2.html](../../scripts/csharp-dotnet-tutorial-2.html) 使用的動手練習檔案。這裡不是正式 study module，只是個人練習區（類似 `demo/hello-csharp/`）。

每個檔案都是 .NET 10 file-based app，可以直接執行，不需要 `.csproj`：

```powershell
cd demo/csharp-practice
dotnet run 00-function.cs   # 跟第 1 節 TODO 填完先會有輸出
dotnet run 01-record.cs
dotnet run 02-list-linq.cs
dotnet run 03-nullable.cs
dotnet run 04-async.cs
dotnet run 05-error-handling.cs
dotnet run 06-capstone.cs   # 依 TODO 順序自己填完先會通過
```

`06-capstone.cs` 刻意留空（只有 TODO 註解），要自己跟教學逐步填完。完整答案在教學頁第 7 節的可展開區塊。

## concepts/ 子資料夾

`concepts/` 內的檔案配合五個獨立嘅深入教學（每個都係 `scripts/csharp-dotnet-tutorial-2.html` 第 1 節其中一個概念嘅大學程度版本），一樣係 TODO 骨架，答案在對應教學頁嘅「自己動手」可展開區塊：

```powershell
cd demo/csharp-practice
dotnet run concepts/01-types-practice.cs               # scripts/csharp-concept-01-types.html
dotnet run concepts/02-naming-practice.cs               # scripts/csharp-concept-02-naming.html
dotnet run concepts/03-methods-practice.cs              # scripts/csharp-concept-03-methods.html
dotnet run concepts/04-generics-practice.cs             # scripts/csharp-concept-04-generics.html
dotnet run concepts/05-class-record-struct-practice.cs  # scripts/csharp-concept-05-class-record-struct.html
```
