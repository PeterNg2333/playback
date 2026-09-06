[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$studyScript = Join-Path $PSScriptRoot 'study.ps1'
& $studyScript | Out-Host

$summaryPath = Join-Path $PSScriptRoot '.artifacts\summary.json'
if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
    throw 'Expected study.ps1 to create .artifacts\summary.json.'
}

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
$actual = @($summary.chunks | ForEach-Object { $_.classification })
$expected = @('SILENCE', 'SPEECH', 'SILENCE', 'SPEECH')

if ($actual.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) chunks, got $($actual.Count)."
}

for ($index = 0; $index -lt $expected.Count; $index++) {
    if ($actual[$index] -ne $expected[$index]) {
        throw "Chunk $index should be $($expected[$index]), got $($actual[$index])."
    }
}

Write-Host 'PASS: deterministic PCM16 WAV was chunked and classified as expected.' -ForegroundColor Green
