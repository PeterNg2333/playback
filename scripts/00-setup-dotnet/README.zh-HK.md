# 00 — 準備 .NET 10 開發環境

這個 folder 是環境準備的第一課。`study.ps1` 預設只讀取版本和狀態，**不會安裝或改動任何軟件**。只有你明確加入 `-InstallMissing`，它才會使用 WinGet 安裝缺少的工具。

## 1. 只檢查（先做這一步）

在 VS Code 打開 repo folder，選單 Terminal → New Terminal，然後執行：

```powershell
.\scripts\00-setup-dotnet\study.ps1
```

你會看到四項結果：

- `WinGet`：Windows Package Manager；安裝軟件用。
- `.NET SDK 10`：編譯和執行 C# 用。
- `Visual Studio Code`：編輯器。
- `Docker`：稍後在本機啟動 PostgreSQL container 用。

`Docker` 的 `Installed / engine stopped` 不代表安裝壞了，只代表 Docker Desktop 尚未啟動。先打開 Docker Desktop，等狀態顯示 engine running，再重跑檢查。

如果 PowerShell 因 execution policy 拒絕執行 `.ps1`，可只為這一次新 process 執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\00-setup-dotnet\study.ps1
```

這不會改寫電腦或使用者的永久 execution policy。Microsoft 說明 Process scope 只在該 session 存活，關閉後便消失：<https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_execution_policies>

## 2. 預覽安裝，不作任何改動

```powershell
.\scripts\00-setup-dotnet\study.ps1 -InstallMissing -WhatIf
```

`-WhatIf` 會列出本來要安裝的 package，但不執行安裝。這是認識 PowerShell safe-by-default command 的好方法。

## 3. 明確安裝缺少項目

看過預覽後才執行：

```powershell
.\scripts\00-setup-dotnet\study.ps1 -InstallMissing
```

固定 package ID 如下，並使用 WinGet 的 `--id --exact --source winget` 避免 substring 選錯 package：

| 工具 | WinGet package ID |
| --- | --- |
| .NET 10 SDK | `Microsoft.DotNet.SDK.10` |
| Visual Studio Code | `Microsoft.VisualStudioCode` |
| Docker Desktop | `Docker.DockerDesktop` |

WinGet 官方文件建議用 `--id` 加 `--exact` 消除歧義：<https://learn.microsoft.com/windows/package-manager/winget/install>

安裝程式可能要求 UAC、接受 Docker 授權條款或重新開機。完成後**關閉舊 terminal，再開新 terminal**，讓 PATH 更新，然後再次只檢查。

如果 WinGet 本身缺少，先安裝／修復 Microsoft App Installer；腳本不會自行從未知網址下載 executable。官方入口：<https://learn.microsoft.com/windows/package-manager/winget/>

## SDK、Runtime、CLI 有何不同？

| 名稱 | 心智模型 | 這個 POC 要不要？ |
| --- | --- | --- |
| .NET Runtime | 「播放機」：只足夠執行已編譯 app | SDK 已包含相應 runtime，不用另裝 |
| .NET SDK | 「工作台」：compiler、templates、build tools、runtime | 要，版本 10 |
| .NET CLI | `dotnet` command，是 SDK 的主要入口 | 要；用它 build、run、test |

Microsoft 定義 SDK 包含 .NET CLI、runtime、libraries 和 `dotnet` driver：<https://learn.microsoft.com/dotnet/core/sdk>

常用命令：

```powershell
dotnet --list-sdks       # 已安裝哪些 SDK
dotnet --list-runtimes   # 已安裝哪些 runtime
dotnet --info            # 完整環境診斷
dotnet --help            # CLI 幫助
```

.NET 10 是 LTS，Microsoft 目前列出的支援期至 2028 年 11 月：<https://learn.microsoft.com/dotnet/core/releases-and-support>

## VS Code 的第一次 C# 開發

1. 在 VS Code 按 `Ctrl+Shift+X` 開 Extensions。
2. 搜尋 `C# Dev Kit`，確認 publisher 是 Microsoft，再安裝。
3. File → Open Folder，打開整個 `playback` repo，而非只打開單一檔案。
4. Terminal → New Terminal，執行下一課：

   ```powershell
   .\scripts\01-csharp-basics\study.ps1
   ```

5. 打開 `scripts/01-csharp-basics/basics.cs`；把 cursor 放在 type 上看 IntelliSense，按 `F12` 看定義。
6. 日後進入正式 project，才用 Run and Debug 或 `dotnet test`。本課是單檔 spike，刻意不先引入 `.csproj`。

VS Code 官方把 VS Code、C# Dev Kit 和 .NET SDK 列為必要工具：<https://code.visualstudio.com/docs/csharp/get-started>

## Docker 為何現在要裝、但這課不用啟 container？

之後 PostgreSQL 會放在 Docker Compose，避免初學階段把資料庫安裝散落在 Windows。這一課只分辨三個狀態：

```text
Missing                    沒有 Docker CLI
Detected / unavailable     找到 executable，但此 terminal 無法執行
Installed / engine stopped CLI 可用，但 Docker Desktop engine 未啟動
Installed / running        CLI 和 engine 都可用
```

Docker Desktop 的 Windows 安裝、WSL 2／Hyper-V 和授權要求，以官方文件為準：<https://docs.docker.com/desktop/setup/install/windows-install/>

## 驗證教材本身

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\00-setup-dotnet\verify.ps1
```

驗證會執行 read-only checker，確認四個工具都有報告欄位；亦會執行 `-InstallMissing -WhatIf`，證明安裝預覽不會真的安裝。最後它會靜態檢查 HTML 的 language、viewport、skip link、單一 `h1`，以及沒有 script／外部 assets。
