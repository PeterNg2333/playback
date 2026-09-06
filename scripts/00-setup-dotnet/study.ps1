[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [switch]$InstallMissing,
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {

function Find-Application {
    param([string]$Name, [string[]]$FallbackPaths = @())

    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) { return $command.Source }

    foreach ($path in $FallbackPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $path).Path
        }
    }

    return $null
}

function Invoke-Tool {
    param([string]$Path, [string[]]$Arguments)

    if (-not $Path) { return $null }
    try {
        $output = @(& $Path @Arguments 2>$null)
        if ($LASTEXITCODE -eq 0) { return $output }
    }
    catch { return $null }

    return $null
}

function Get-ToolReport {
    $wingetPath = Find-Application 'winget.exe' @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'))
    $dotnetPath = Find-Application 'dotnet.exe' @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'))
    $codePath = Find-Application 'code.cmd' @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'),
        (Join-Path $env:ProgramFiles 'Microsoft VS Code\bin\code.cmd'))
    $dockerPath = Find-Application 'docker.exe' @(
        (Join-Path $env:ProgramFiles 'Docker\Docker\resources\bin\docker.exe'))

    $report = [System.Collections.Generic.List[object]]::new()
    $wingetVersion = @(Invoke-Tool $wingetPath @('--version')) | Select-Object -First 1
    $report.Add([pscustomobject]@{
        Name = 'WinGet'; Status = if ($wingetVersion) { 'Installed' } elseif ($wingetPath) { 'Detected / unavailable' } else { 'Missing' }
        Details = if ($wingetVersion) { $wingetVersion } elseif ($wingetPath) { 'App execution alias exists but could not run in this terminal.' } else { 'Install Microsoft App Installer first.' }
    })

    $sdks = @(Invoke-Tool $dotnetPath @('--list-sdks'))
    $sdk10 = @($sdks | Where-Object { $_ -match '^10\.[0-9]+\.[0-9]+' })
    $report.Add([pscustomobject]@{
        Name = '.NET SDK 10'; Status = if ($sdk10.Count -gt 0) { 'Installed' } else { 'Missing' }
        Details = if ($sdk10.Count -gt 0) { $sdk10 -join '; ' } else { 'Required for .NET 10 development.' }
    })

    $codeExecutable = if ($codePath) {
        Join-Path (Split-Path (Split-Path $codePath -Parent) -Parent) 'Code.exe'
    } else { $null }
    $codeVersion = if ($codeExecutable -and (Test-Path -LiteralPath $codeExecutable -PathType Leaf)) {
        (Get-Item -LiteralPath $codeExecutable).VersionInfo.ProductVersion
    } else { $null }
    $report.Add([pscustomobject]@{
        Name = 'Visual Studio Code'; Status = if ($codePath) { 'Installed' } else { 'Missing' }
        Details = if ($codeVersion) { "Version $codeVersion" } elseif ($codePath) { 'Launcher found; file version unavailable.' } else { 'Editor is not on PATH or a known install path.' }
    })

    $dockerOutput = @(Invoke-Tool $dockerPath @('--version')) | Select-Object -First 1
    $dockerVersion = if ($dockerOutput) { $dockerOutput -replace '^Docker version\s+|,.*$' } else { $null }
    $daemonRunning = $false
    if ($dockerPath) {
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        try {
            $null = & $dockerPath info --format '{{.ServerVersion}}' 2>$null
            $daemonRunning = $LASTEXITCODE -eq 0
        }
        finally { $ErrorActionPreference = $previousPreference }
    }
    $report.Add([pscustomobject]@{
        Name = 'Docker'; Status = if (-not $dockerPath) { 'Missing' } elseif (-not $dockerVersion) { 'Detected / unavailable' } elseif ($daemonRunning) { 'Installed / running' } else { 'Installed / engine stopped' }
        Details = if ($dockerVersion) { "CLI $dockerVersion" } elseif ($dockerPath) { 'Executable exists but could not run in this terminal.' } else { 'Docker Desktop is not on a known install path.' }
    })

    return $report
}

$report = @(Get-ToolReport)
if ($InstallMissing) {
    $winget = Find-Application 'winget.exe' @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'))
    $wingetVersion = @(Invoke-Tool $winget @('--version')) | Select-Object -First 1
    $packages = [ordered]@{
        '.NET SDK 10' = 'Microsoft.DotNet.SDK.10'
        'Visual Studio Code' = 'Microsoft.VisualStudioCode'
        'Docker' = 'Docker.DockerDesktop'
    }
    foreach ($tool in $packages.Keys) {
        $current = $report | Where-Object Name -eq $tool
        if ($current.Status -eq 'Missing' -and $PSCmdlet.ShouldProcess($packages[$tool], 'Install with WinGet')) {
            if (-not $wingetVersion) {
                throw 'WinGet cannot run. Install or repair Microsoft App Installer, then retry.'
            }
            & $winget install --id $packages[$tool] --exact --source winget `
                --accept-package-agreements --accept-source-agreements | Out-Host
            if ($LASTEXITCODE -ne 0) { Write-Warning "WinGet could not install $tool (exit $LASTEXITCODE)." }
        }
    }

    Write-Host 'Install/preview pass finished. After a real install, open a new terminal and check again.' -ForegroundColor Yellow
}

if ($PassThru) { $report } else { $report | Format-Table -AutoSize }
}
finally {
    Pop-Location
}
