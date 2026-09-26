[CmdletBinding()]
param([switch]$OpenLesson)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$required = @('README.zh-HK.md', 'study.ps1', 'index.html')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) {
        throw "Missing study file: $name"
    }
}

$html = Get-Content -LiteralPath (Join-Path $root 'index.html') -Raw -Encoding utf8
foreach ($marker in @('<main', 'id="notes"', 'id="transcript"', 'id="sources"', 'id="chat-panel"', 'id="explanation"')) {
    if (-not $html.Contains($marker)) { throw "Missing UI region: $marker" }
}
if ($html -match 'https?://|fetch\s*\(|XMLHttpRequest|<form[^>]+action=') {
    throw 'The default demo must remain offline.'
}
$ids = @([regex]::Matches($html, '\bid="(?<value>[^"]+)"') | ForEach-Object { $_.Groups['value'].Value })
if (($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'Duplicate HTML id found.' }
foreach ($match in [regex]::Matches($html, 'data-jump="(?<value>[^"]+)"')) {
    if ($ids -notcontains $match.Groups['value'].Value) {
        throw "Broken citation target: $($match.Groups['value'].Value)"
    }
}
foreach ($match in [regex]::Matches($html, 'data-question="(?<value>[^"]+)"')) {
    $key = [regex]::Escape($match.Groups['value'].Value)
    if ($html -notmatch "(?m)^\s*${key}:\s*\{") {
        throw "Missing sample answer: $($match.Groups['value'].Value)"
    }
}
$notesMarkup = [regex]::Match($html, '<section class="panel notes-panel".*?</section>', 'Singleline').Value
if ($notesMarkup -match 'data-jump=|class="citation"') { throw 'Key points must not contain source references.' }
foreach ($theme in @('pulse', 'wave', 'loop')) {
    if (-not $html.Contains("id=`"icon-$theme`"") -or -not $html.Contains("data-theme=`"$theme`"")) {
        throw "Missing Playback theme or icon: $theme"
    }
}
$hasRanges = [regex]::IsMatch($html, '<span class="time-range">\d\d:\d\d\u2013\d\d:\d\d</span>')
$hasEditor = $html.Contains('id="note-editor"')
$hasPreview = $html.Contains('id="note-preview"')
$hasFlowchart = $html.Contains('```flowchart') -and $html.Contains('function renderFlowchart(')
if (-not $hasRanges -or -not $hasEditor -or -not $hasPreview -or -not $hasFlowchart) {
    throw 'Markdown flowchart or transcript time ranges are missing.'
}

Write-Host 'PASS: offline UI design demo is ready.' -ForegroundColor Green
Write-Host 'Open scripts/ui-design-demo/index.html to inspect the proposal.'
if ($OpenLesson) { Start-Process -FilePath (Join-Path $root 'index.html') }
