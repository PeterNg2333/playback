[CmdletBinding()]
param(
    [ValidateSet('Demo', 'Gemini', 'Inspect')]
    [string]$Mode = 'Demo',

    [ValidateSet('gemini-3.5-flash-lite', 'gemini-3.8-flash')]
    [string]$Model = 'gemini-3.5-flash-lite',

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Find-DotNet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $windowsDefault = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $windowsDefault -PathType Leaf) { return $windowsDefault }

    throw 'The .NET SDK was not found. Run module 00, then reopen the VS Code terminal.'
}

if ($Mode -eq 'Inspect') {
    Write-Host 'Offline demo: rolling-note-demo.cs' -ForegroundColor Cyan
    Write-Host 'Online opt-in: Google.GenAI 1.21.0 + Microsoft.Agents.AI 1.20.0'
    Write-Host 'Secret variable: GOOGLE_AI_STUDIO_API_KEY (value is never printed)'
    Write-Host 'Default model: gemini-3.5-flash-lite; stronger option: gemini-3.8-flash'
    return
}

$dotnet = Find-DotNet
if ($Mode -eq 'Demo') {
    $demoPath = Join-Path $PSScriptRoot 'rolling-note-demo.cs'
    $arguments = @('run', '--file', $demoPath)
    if ($SelfTest) { $arguments += @('--', '--self-test') }
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Offline note demo failed with exit code $LASTEXITCODE." }
    return
}

if ([string]::IsNullOrWhiteSpace($env:GOOGLE_AI_STUDIO_API_KEY)) {
    throw 'Set GOOGLE_AI_STUDIO_API_KEY in this PowerShell process before choosing -Mode Gemini.'
}

$previousNugetPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path (Split-Path $PSScriptRoot -Parent) '.nuget\packages'
try {
    $appPath = Join-Path $PSScriptRoot 'gemini-note-spike.cs'
    $transcriptPath = Join-Path $PSScriptRoot 'sample-transcript.txt'
    $materialPath = Join-Path $PSScriptRoot 'sample-material.txt'
    & $dotnet run --file $appPath -- `
        --model $Model `
        --transcript $transcriptPath `
        --material $materialPath
    if ($LASTEXITCODE -ne 0) { throw "Gemini spike failed with exit code $LASTEXITCODE." }
}
finally {
    $env:NUGET_PACKAGES = $previousNugetPackages
}

