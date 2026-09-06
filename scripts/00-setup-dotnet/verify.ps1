$ErrorActionPreference = 'Stop'

$studyScript = Join-Path $PSScriptRoot 'study.ps1'
if (-not (Test-Path -LiteralPath $studyScript -PathType Leaf)) {
    throw 'RED: study.ps1 does not exist yet.'
}

$results = @(& $studyScript -PassThru)
$expected = @('WinGet', '.NET SDK 10', 'Visual Studio Code', 'Docker')

foreach ($name in $expected) {
    $result = $results | Where-Object Name -eq $name
    if ($null -eq $result) {
        throw "Missing environment check: $name"
    }

    if ([string]::IsNullOrWhiteSpace($result.Status)) {
        throw "Environment check has no status: $name"
    }
}

# WhatIf must remain a safe preview even when an App Execution Alias is detected
# but cannot run inside a restricted terminal.
$preview = @(& $studyScript -InstallMissing -WhatIf -PassThru)
if ($preview.Count -lt 4) {
    throw 'The install preview did not return the complete environment report.'
}

$htmlPath = Join-Path $PSScriptRoot 'index.html'
$html = Get-Content -LiteralPath $htmlPath -Raw -Encoding UTF8
foreach ($requiredFragment in @(
    '<html lang="zh-Hant">',
    'name="viewport"',
    'class="skip" href="#main"',
    '<main id="main">',
    '@media (max-width:',
    'Microsoft.DotNet.SDK.10'
)) {
    if (-not $html.Contains($requiredFragment)) {
        throw "index.html is missing: $requiredFragment"
    }
}

if ([regex]::Matches($html, '<h1\b', 'IgnoreCase').Count -ne 1) {
    throw 'index.html must contain exactly one h1.'
}

if ($html -match '(?i)<script\b|<link\b|\b(?:src|poster)\s*=|url\(\s*["'']?https?:') {
    throw 'index.html must not load scripts or external assets.'
}

Write-Host 'PASS: setup checker reports all four tools without installing anything.' -ForegroundColor Green
