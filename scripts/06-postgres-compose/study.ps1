[CmdletBinding()]
param(
    [ValidateSet('Check', 'Start', 'Status', 'Query', 'Stop')]
    [string]$Action = 'Check',
    [switch]$OpenLesson
)

$ErrorActionPreference = 'Stop'
$moduleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$composePath = Join-Path $moduleRoot 'compose.yaml'
$schemaPath = Join-Path $moduleRoot 'schema.sql'
$queryPath = Join-Path $moduleRoot 'queries.sql'
$lessonPath = Join-Path $moduleRoot 'index.html'
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue

foreach ($requiredPath in @($composePath, $schemaPath, $queryPath, $lessonPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required study file was not found: $requiredPath"
    }
}

if (-not $dockerCommand) {
    throw 'Docker CLI was not found. Install and start Docker Desktop first.'
}

$dockerExe = $dockerCommand.Source

function Invoke-Compose {
    param([string[]]$ComposeArguments)

    & $dockerExe compose --file $composePath @ComposeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE."
    }
}

function Test-ComposeConfiguration {
    & $dockerExe compose version
    if ($LASTEXITCODE -ne 0) {
        throw 'The Docker Compose plugin is unavailable. Update Docker Desktop.'
    }

    Invoke-Compose @('config', '--quiet')
}

Write-Host 'Module 06 - PostgreSQL + Docker Compose spike' -ForegroundColor Cyan
Write-Host 'Scope: local study only; this is not a Playback runtime migration.'
Write-Host 'Connection: 127.0.0.1:55432 / playback_spike / playback_demo'
Write-Host 'WARNING: compose.yaml contains a public demo password. Never reuse it.' -ForegroundColor Yellow
Write-Host ''

switch ($Action) {
    'Check' {
        & $dockerExe --version
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker CLI could not run.'
        }

        Test-ComposeConfiguration
        Write-Host 'PASS: Compose syntax and module paths are valid.' -ForegroundColor Green

        & $dockerExe info --format '{{.ServerVersion}}'
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'PASS: Docker engine is reachable.' -ForegroundColor Green
        } else {
            Write-Warning 'Docker CLI is installed, but the engine did not respond. Start Docker Desktop before using -Action Start.'
        }
    }

    'Start' {
        Test-ComposeConfiguration
        Write-Host 'Starting PostgreSQL. The first run must download the official image.' -ForegroundColor Yellow
        Invoke-Compose @('up', '--detach', '--wait')
        Write-Host 'PASS: PostgreSQL healthcheck succeeded.' -ForegroundColor Green
        Invoke-Compose @('ps')
        Write-Host 'NOTE: schema.sql runs only when the data volume is empty.'
    }

    'Status' {
        Test-ComposeConfiguration
        Invoke-Compose @('ps')
        Write-Host 'Healthy means PostgreSQL accepts connections; running alone is not ready.'
    }

    'Query' {
        Test-ComposeConfiguration
        if (-not (Test-Path -LiteralPath $queryPath -PathType Leaf)) {
            throw "Query lesson was not found: $queryPath"
        }

        & $dockerExe compose --file $composePath exec --no-TTY postgres `
            pg_isready --username playback_demo --dbname playback_spike
        if ($LASTEXITCODE -ne 0) {
            throw 'PostgreSQL is not ready. Run .\study.ps1 -Action Start first.'
        }

        $sessionId = [guid]::NewGuid().ToString()
        $lectureTitle = "Playback SQL spike $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        Write-Host "Study session id: $sessionId"

        & $dockerExe compose --file $composePath exec --no-TTY postgres `
            psql `
            --username playback_demo `
            --dbname playback_spike `
            --set ON_ERROR_STOP=1 `
            --set "session_id=$sessionId" `
            --set "lecture_title=$lectureTitle" `
            --file /study/queries.sql
        if ($LASTEXITCODE -ne 0) {
            throw "SQL lesson failed with exit code $LASTEXITCODE."
        }

        Write-Host 'PASS: Transaction, JOIN, prepared query, and EXPLAIN ran.' -ForegroundColor Green
    }

    'Stop' {
        Test-ComposeConfiguration
        Invoke-Compose @('stop', '--timeout', '30')
        Write-Host 'PASS: Container stopped; the named volume and study data remain.' -ForegroundColor Green
        Write-Host 'This module intentionally provides no Reset or volume-delete action.'
    }
}

if ($OpenLesson) {
    Start-Process -FilePath $lessonPath
}

Write-Host ''
Write-Host 'Actions: Check (default), Start, Status, Query, Stop.'
