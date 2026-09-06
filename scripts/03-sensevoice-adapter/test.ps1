[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$studyScript = Join-Path $PSScriptRoot 'study.ps1'
& $studyScript | Out-Host

$planPath = Join-Path $PSScriptRoot '.artifacts\request-plan.json'
if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
    throw 'Expected dry-run to create .artifacts\request-plan.json.'
}

$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
if ($plan.send -ne $false) { throw 'Default execution must be a dry-run.' }
if ($plan.method -ne 'POST') { throw 'SenseVoice request must use POST.' }
if ($plan.endpoint -ne 'https://dev-aks.setsailapi.com/stt/infer/upload') {
    throw "Unexpected endpoint: $($plan.endpoint)"
}
if ($plan.multipartField -ne 'file') { throw "Multipart field must be 'file'." }
if ($plan.contentType -ne 'audio/wav') { throw "File content type must be 'audio/wav'." }
if ($plan.allowAutoRedirect -ne $false) {
    throw 'The request plan must explicitly disable automatic redirects.'
}
if ($plan.sha256.Length -ne 64) { throw 'Expected a SHA-256 checksum in the request plan.' }

$studySource = Get-Content -LiteralPath $studyScript -Raw -Encoding UTF8
if ($studySource -notmatch '\$handler\.AllowAutoRedirect\s*=\s*\$false') {
    throw 'The HTTP handler must disable automatic redirects before audio is uploaded.'
}
if ($studySource -notmatch '\$handler\.Dispose\(\)') {
    throw 'The HTTP handler must be disposed explicitly.'
}

$artifactRoot = Join-Path $PSScriptRoot '.artifacts'
$invalidExtension = Join-Path $artifactRoot 'not-a-wav.txt'
Copy-Item -LiteralPath $plan.wavPath -Destination $invalidExtension -Force
$extensionWasRejected = $false
try {
    & $studyScript -WavPath $invalidExtension | Out-Null
}
catch {
    $extensionWasRejected = $_.Exception.Message -like 'Only a .wav file*'
}
if (-not $extensionWasRejected) { throw 'Expected a non-.wav extension to be rejected.' }

$oversizedWav = Join-Path $artifactRoot 'oversized.wav'
$oversizedStream = [System.IO.File]::Open($oversizedWav, [System.IO.FileMode]::Create)
try { $oversizedStream.SetLength(25MB + 1) }
finally { $oversizedStream.Dispose() }
$sizeWasRejected = $false
try {
    & $studyScript -WavPath $oversizedWav | Out-Null
}
catch {
    $sizeWasRejected = $_.Exception.Message -like 'WAV exceeds*'
}
if (-not $sizeWasRejected) { throw 'Expected a WAV over 25 MiB to be rejected.' }

Remove-Item -LiteralPath $invalidExtension, $oversizedWav -Force
Write-Host 'PASS: default mode inspected a valid WAV without sending a network request.' -ForegroundColor Green
Write-Host 'PASS: invalid extension and oversized input were rejected.' -ForegroundColor Green
Write-Host 'PASS: automatic redirects are disabled and the HTTP handler is explicitly disposed.' -ForegroundColor Green
