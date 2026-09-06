[CmdletBinding()]
param(
    [ValidateSet('Demo', 'Wikipedia', 'Inspect')]
    [string]$Mode = 'Demo',

    [ValidateLength(1, 200)]
    [string]$Query = 'voice activity detection',

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
    Write-Host 'Search host allowlist: en.wikipedia.org' -ForegroundColor Cyan
    Write-Host 'Maximum query length: 200 characters; maximum evidence results: 3'
    Write-Host 'Default Demo mode reads sample-search.json and makes no network request.'
    Write-Host 'This module prepares evidence; it does not pretend that evidence is an LLM answer.'
    return
}

$dotnet = Find-DotNet
$appPath = Join-Path $PSScriptRoot 'search-spike.cs'
$arguments = @('run', '--file', $appPath, '--')

if ($Mode -eq 'Demo') {
    $arguments += @('--mode', 'demo', '--query', $Query, '--sample', (Join-Path $PSScriptRoot 'sample-search.json'))
}
else {
    $arguments += @('--mode', 'wikipedia', '--query', $Query)
}
if ($SelfTest) { $arguments += '--self-test' }

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Search spike failed with exit code $LASTEXITCODE." }

