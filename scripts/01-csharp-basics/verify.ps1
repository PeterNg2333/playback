$ErrorActionPreference = 'Stop'

$studyScript = Join-Path $PSScriptRoot 'study.ps1'
if (-not (Test-Path -LiteralPath $studyScript -PathType Leaf)) {
    throw 'RED: study.ps1 does not exist yet.'
}

# Test the same entry point a learner runs, including SDK detection and file mode.
$output = @(& $studyScript 2>&1)
$joinedOutput = $output -join [Environment]::NewLine

foreach ($marker in @('[types]', '[record]', '[list+linq]', '[nullable]', '[async]', '[error]', 'PASS')) {
    if (-not $joinedOutput.Contains($marker)) {
        throw "basics.cs output is missing marker: $marker"
    }
}

$sourcePreview = @(& $studyScript -ShowSource *>&1) -join [Environment]::NewLine
$expectedChinese = -join @(
    [char]0x672A, [char]0x63D0, [char]0x4F9B, [char]0x8B1B,
    [char]0x5E2B, [char]0x540D, [char]0x7A31)
$expectedSourceLine = 'string lecturerLabel = lecturerName?.Trim() ?? "' + [char]0xFF08 + $expectedChinese
if (-not $sourcePreview.Contains($expectedSourceLine)) {
    throw 'The -ShowSource preview must preserve UTF-8 Chinese text.'
}

$htmlPath = Join-Path $PSScriptRoot 'index.html'
$html = Get-Content -LiteralPath $htmlPath -Raw -Encoding UTF8
foreach ($requiredFragment in @(
    '<html lang="zh-Hant">',
    'name="viewport"',
    'class="skip" href="#content"',
    '<main id="content">',
    '@media (max-width:',
    '<code>error handling</code>'
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

Write-Host 'PASS: the .NET 10 file-based C# sample ran every lesson.' -ForegroundColor Green
