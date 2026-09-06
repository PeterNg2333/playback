[CmdletBinding()]
param([switch]$ShowSource)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {

$sample = Join-Path $PSScriptRoot 'basics.cs'
$dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
$dotnetPath = if ($null -ne $dotnetCommand) { $dotnetCommand.Source } else { $null }
if ($null -eq $dotnetPath -and (Test-Path -LiteralPath 'C:\Program Files\dotnet\dotnet.exe' -PathType Leaf)) {
    $dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
}
if ($null -eq $dotnetPath) {
    throw '.NET SDK was not found. Run scripts\00-setup-dotnet\study.ps1 first.'
}

$installedSdks = @(& $dotnetPath --list-sdks)
$sdk10 = @($installedSdks | Where-Object { $_ -match '^10\.[0-9]+\.[0-9]+' })
if ($sdk10.Count -eq 0) {
    throw '.NET SDK 10 is required for file-based apps.'
}

if ($ShowSource) {
    Write-Host '--- basics.cs ------------------------------------------------' -ForegroundColor Cyan
    $lineNumber = 0
    Get-Content -LiteralPath $sample -Encoding UTF8 | ForEach-Object {
        $lineNumber++
        '{0,3} | {1}' -f $lineNumber, $_
    }
    Write-Host '--- output ---------------------------------------------------' -ForegroundColor Cyan
}

# --file is explicit, so a future .csproj in the current directory cannot steal this invocation.
# Source: https://learn.microsoft.com/dotnet/core/sdk/file-based-apps#run-applications
& $dotnetPath run --file $sample
if ($LASTEXITCODE -ne 0) {
    throw "The C# sample failed with exit code $LASTEXITCODE."
}
}
finally {
    Pop-Location
}
