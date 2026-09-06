[CmdletBinding()]
param(
    [switch]$RunStudies
)

$ErrorActionPreference = 'Stop'
$scriptsRoot = $PSScriptRoot
$expectedModules = @(
    '00-setup-dotnet',
    '01-csharp-basics',
    '02-audio-vad',
    '03-sensevoice-adapter',
    '04-live-note-agent',
    '05-live-chat-search',
    '06-postgres-compose',
    '07-poc-roadmap'
)
$requiredFiles = @('README.zh-HK.md', 'study.ps1', 'index.html')
$failures = New-Object System.Collections.Generic.List[string]

Write-Host 'ASR + LLM Note spike verification' -ForegroundColor Cyan

foreach ($moduleName in $expectedModules) {
    $modulePath = Join-Path $scriptsRoot $moduleName
    if (-not (Test-Path -LiteralPath $modulePath -PathType Container)) {
        $failures.Add("Missing module: $moduleName")
        continue
    }

    foreach ($requiredFile in $requiredFiles) {
        $filePath = Join-Path $modulePath $requiredFile
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            $failures.Add("Missing: $moduleName/$requiredFile")
        }
    }
}

$parseErrors = @()
Get-ChildItem -LiteralPath $scriptsRoot -Recurse -Filter '*.ps1' -File | ForEach-Object {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $_.FullName,
        [ref]$tokens,
        [ref]$errors
    )
    foreach ($parseError in $errors) {
        $parseErrors += "PowerShell parse error in $($_.FullName): $($parseError.Message)"
    }
}
$parseErrors | ForEach-Object { $failures.Add($_) }

Get-ChildItem -LiteralPath $scriptsRoot -Recurse -Filter 'index.html' -File | ForEach-Object {
    $html = Get-Content -LiteralPath $_.FullName -Raw
    foreach ($marker in @('<html', 'lang=', '<meta name="viewport"', '<title', '<main')) {
        if ($html.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $failures.Add("HTML marker '$marker' missing in $($_.FullName)")
        }
    }

    if ($html -match '<(?:script|img|iframe|audio|video|source)[^>]+src\s*=\s*["'']https?://' -or
        $html -match '<link[^>]+href\s*=\s*["'']https?://' -or
        $html -match '@import\s+(?:url\()?\s*["'']?https?://' -or
        $html -match 'url\(\s*["'']?https?://' -or
        $html -match 'fetch\(\s*["'']https?://') {
        $failures.Add("Remote runtime asset or fetch found in $($_.FullName); study pages must work offline.")
    }

    $hrefMatches = [regex]::Matches(
        $html,
        'href\s*=\s*["''](?<href>[^"'']+)["'']',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )
    foreach ($hrefMatch in $hrefMatches) {
        $href = $hrefMatch.Groups['href'].Value
        if ($href -match '^(https?:|mailto:|tel:|#)' ) { continue }

        $localPart = ($href -split '[?#]', 2)[0]
        if ([string]::IsNullOrWhiteSpace($localPart)) { continue }
        $decodedPart = [Uri]::UnescapeDataString($localPart).Replace('/', [IO.Path]::DirectorySeparatorChar)
        $targetPath = Join-Path $_.DirectoryName $decodedPart
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            $failures.Add("Broken local link '$href' in $($_.FullName)")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL  $_" -ForegroundColor Red }
    throw "Structure verification failed with $($failures.Count) issue(s)."
}

Write-Host 'PASS  module structure, PowerShell syntax and HTML markers' -ForegroundColor Green

if ($RunStudies) {
    foreach ($moduleName in $expectedModules) {
        $entryName = 'study.ps1'
        $entryArguments = @{}
        switch ($moduleName) {
            '00-setup-dotnet' { $entryName = 'verify.ps1' }
            '01-csharp-basics' { $entryName = 'verify.ps1' }
            '02-audio-vad' { $entryName = 'test.ps1' }
            '03-sensevoice-adapter' { $entryName = 'test.ps1' }
            '04-live-note-agent' { $entryArguments = @{ SelfTest = $true } }
            '05-live-chat-search' { $entryArguments = @{ SelfTest = $true } }
            '07-poc-roadmap' { $entryArguments = @{ Strict = $true } }
        }

        $entryPath = Join-Path (Join-Path $scriptsRoot $moduleName) $entryName
        Write-Host "`n--- $moduleName ($entryName; safe verification) ---" -ForegroundColor Yellow
        try {
            & $entryPath @entryArguments
        }
        catch {
            throw "$moduleName verification failed: $($_.Exception.Message)"
        }
    }
    Write-Host "`nPASS  all safe module verifications completed" -ForegroundColor Green
}
