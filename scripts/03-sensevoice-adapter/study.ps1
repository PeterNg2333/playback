[CmdletBinding()]
param(
    [string]$WavPath,

    [switch]$Send,

    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$endpoint = [Uri]'https://dev-aks.setsailapi.com/stt/infer/upload'
$maxWavBytes = 25MB
$maxResponseBytes = 1MB
$artifactRoot = Join-Path $PSScriptRoot '.artifacts'
[System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null

function New-SilentProbeWav {
    param([Parameter(Mandatory)] [string]$Path)

    $sampleRate = 16000
    $sampleCount = 4000 # 250 ms; deliberately not real speech
    $dataLength = $sampleCount * 2
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int](36 + $dataLength))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]1)
        $writer.Write([int]$sampleRate)
        $writer.Write([int]($sampleRate * 2))
        $writer.Write([int16]2)
        $writer.Write([int16]16)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$dataLength)
        for ($index = 0; $index -lt $sampleCount; $index++) {
            $writer.Write([int16]0)
        }
        $writer.Flush()
        $stream.Flush($true)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Assert-WavFile {
    param([Parameter(Mandatory)] [string]$Path)

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Extension -ine '.wav') {
        throw "Only a .wav file can be sent: $($item.FullName)"
    }
    if ($item.Length -lt 44) {
        throw 'WAV is too short to contain a valid PCM header.'
    }
    if ($item.Length -gt $maxWavBytes) {
        throw "WAV exceeds the $maxWavBytes-byte study limit. Split it into short chunks first."
    }

    $stream = [System.IO.File]::OpenRead($item.FullName)
    try {
        $header = [byte[]]::new(12)
        if ($stream.Read($header, 0, $header.Length) -ne $header.Length) {
            throw 'Could not read the WAV header.'
        }
        $riff = [System.Text.Encoding]::ASCII.GetString($header, 0, 4)
        $wave = [System.Text.Encoding]::ASCII.GetString($header, 8, 4)
        if ($riff -ne 'RIFF' -or $wave -ne 'WAVE') {
            throw 'The file extension is .wav, but RIFF/WAVE magic bytes are missing.'
        }
    }
    finally {
        $stream.Dispose()
    }

    return $item
}

if ([string]::IsNullOrWhiteSpace($WavPath)) {
    $resolvedWavPath = Join-Path $artifactRoot 'silent-probe.wav'
    New-SilentProbeWav -Path $resolvedWavPath
    Write-Host "Generated a deterministic silent probe: $resolvedWavPath"
}
else {
    $resolvedWavPath = (Resolve-Path -LiteralPath $WavPath -ErrorAction Stop).Path
}

$wav = Assert-WavFile -Path $resolvedWavPath
$sha256 = (Get-FileHash -LiteralPath $wav.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$plan = [pscustomobject]@{
    send = [bool]$Send
    method = 'POST'
    endpoint = $endpoint.AbsoluteUri
    multipartField = 'file'
    uploadFileName = 'audio.wav'
    contentType = 'audio/wav'
    allowAutoRedirect = $false
    wavPath = $wav.FullName
    bytes = $wav.Length
    sha256 = $sha256
    timeoutSeconds = $TimeoutSeconds
}

$planPath = Join-Path $artifactRoot 'request-plan.json'
$plan | ConvertTo-Json | Set-Content -LiteralPath $planPath -Encoding UTF8

Write-Host ''
Write-Host 'SenseVoice request plan'
$plan | Format-List method, endpoint, multipartField, contentType, allowAutoRedirect, wavPath, bytes, sha256, timeoutSeconds

if (-not $Send) {
    Write-Host 'DRY RUN: no network request was sent.' -ForegroundColor Yellow
    Write-Host "Inspect the plan at: $planPath"
    Write-Host 'Add -Send only after you have consent to upload this audio.'
    return
}

Add-Type -AssemblyName System.Net.Http
$handler = $null
$client = $null
$audioStream = $null
$audioContent = $null
$form = $null

try {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    # HttpClient does not own this handler; the finally block disposes both explicitly.
    $client = [System.Net.Http.HttpClient]::new($handler, $false)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    $client.MaxResponseContentBufferSize = $maxResponseBytes
    $client.DefaultRequestHeaders.Accept.Add(
        [System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))

    $audioStream = [System.IO.File]::OpenRead($wav.FullName)
    $audioContent = [System.Net.Http.StreamContent]::new($audioStream)
    $audioContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('audio/wav')
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    $form.Add($audioContent, 'file', 'audio.wav')

    Write-Host "SEND enabled: posting one WAV to $endpoint"
    $response = $client.PostAsync($endpoint, $form).GetAwaiter().GetResult()
    try {
        $responseBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        if ($responseBytes.Length -gt $maxResponseBytes) {
            throw "Response exceeded the $maxResponseBytes-byte study limit."
        }

        $responsePath = Join-Path $artifactRoot 'last-response.body'
        [System.IO.File]::WriteAllBytes($responsePath, $responseBytes)
        $responseContentType = if ($null -eq $response.Content.Headers.ContentType) {
            '(not provided)'
        }
        else {
            $response.Content.Headers.ContentType.ToString()
        }

        $responseMetadata = [pscustomobject]@{
            statusCode = [int]$response.StatusCode
            reasonPhrase = $response.ReasonPhrase
            contentType = $responseContentType
            bytes = $responseBytes.Length
            bodyFile = $responsePath
        }
        $responseMetadata |
            ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $artifactRoot 'last-response-metadata.json') -Encoding UTF8

        Write-Host "Response status: $([int]$response.StatusCode) $($response.ReasonPhrase)"
        Write-Host "Response content type: $responseContentType"
        Write-Host "Response bytes: $($responseBytes.Length)"
        Write-Host "Raw response saved without schema assumptions: $responsePath"

        if (-not $response.IsSuccessStatusCode) {
            throw "SenseVoice returned HTTP $([int]$response.StatusCode). Inspect the saved body as untrusted data."
        }
    }
    finally {
        $response.Dispose()
    }
}
finally {
    if ($null -ne $form) {
        # The multipart form owns and disposes its StreamContent and file stream.
        $form.Dispose()
    }
    elseif ($null -ne $audioContent) {
        $audioContent.Dispose()
    }
    elseif ($null -ne $audioStream) {
        $audioStream.Dispose()
    }

    if ($null -ne $client) {
        $client.Dispose()
    }
    if ($null -ne $handler) {
        $handler.Dispose()
    }
}
