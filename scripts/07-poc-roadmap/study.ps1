[CmdletBinding()]
param(
    [switch]$Strict,
    [switch]$OpenLesson
)

$ErrorActionPreference = 'Stop'
$moduleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$scriptsRoot = (Resolve-Path (Join-Path $moduleRoot '..')).Path
$lessonPath = Join-Path $moduleRoot 'index.html'
$requiredFiles = @('README.zh-HK.md', 'study.ps1', 'index.html')
$inventory = @()

for ($number = 0; $number -le 7; $number++) {
    $prefix = '{0:D2}-' -f $number
    $matches = @(
        Get-ChildItem -LiteralPath $scriptsRoot -Directory |
            Where-Object { $_.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }
    )

    if ($matches.Count -eq 0) {
        $inventory += [pscustomobject]@{
            Number = '{0:D2}' -f $number
            Module = '(missing)'
            README = 'Missing'
            Script = 'Missing'
            HTML = 'Missing'
            Status = 'INCOMPLETE'
        }
        continue
    }

    if ($matches.Count -gt 1) {
        $inventory += [pscustomobject]@{
            Number = '{0:D2}' -f $number
            Module = ($matches.Name -join ', ')
            README = 'Ambiguous'
            Script = 'Ambiguous'
            HTML = 'Ambiguous'
            Status = 'INCOMPLETE'
        }
        continue
    }

    $folder = $matches[0]
    $fileStatus = @{}
    foreach ($requiredFile in $requiredFiles) {
        $filePath = Join-Path $folder.FullName $requiredFile
        $fileStatus[$requiredFile] = if (Test-Path -LiteralPath $filePath -PathType Leaf) {
            'Ready'
        } else {
            'Missing'
        }
    }

    $isReady = $fileStatus.Values -notcontains 'Missing'
    $inventory += [pscustomobject]@{
        Number = '{0:D2}' -f $number
        Module = $folder.Name
        README = $fileStatus['README.zh-HK.md']
        Script = $fileStatus['study.ps1']
        HTML = $fileStatus['index.html']
        Status = if ($isReady) { 'READY' } else { 'INCOMPLETE' }
    }
}

Write-Host 'Module 07 - Spike study inventory' -ForegroundColor Cyan
Write-Host "Scripts root: $scriptsRoot"
Write-Host 'This checks study structure only. READY does not mean the MVP exists.' -ForegroundColor Yellow
Write-Host ''
$inventory | Format-Table -AutoSize

$incomplete = @($inventory | Where-Object { $_.Status -ne 'READY' })
Write-Host ''
Write-Host 'Next step' -ForegroundColor Cyan

if ($incomplete.Count -gt 0) {
    $first = $incomplete[0]
    Write-Host "Complete module $($first.Number) first: $($first.Module)."
    Write-Host 'Every module needs README.zh-HK.md, study.ps1, and index.html.'
    Write-Host 'Run this script again afterward. Complete study files do not prove POC behavior.'
} else {
    Write-Host 'The 00-07 study inventory is complete. Read and run each module in number order.' -ForegroundColor Green
    foreach ($entry in $inventory) {
        Write-Host "  .\scripts\$($entry.Module)\study.ps1"
    }
    Write-Host ''
    Write-Host 'When coding starts, implement only the first simulated vertical slice in the README.'
    Write-Host 'Prove durable chunks, idempotency, and snapshot recovery before Real adapters.'
}

if ($OpenLesson) {
    Start-Process -FilePath $lessonPath
}

if ($Strict -and $incomplete.Count -gt 0) {
    throw "$($incomplete.Count) module(s) failed the inventory gate."
}
