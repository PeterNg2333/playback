[CmdletBinding()]
param(
    [string]$InputPath,

    [ValidateRange(0.001, 1.0)]
    [double]$VadThreshold = 0.02,

    [ValidateRange(1, 30)]
    [int]$ChunkSeconds = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Pcm16MonoWav {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int16[]]$Samples,
        [Parameter(Mandatory)] [int]$SampleRate
    )

    $parent = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $dataLength = $Samples.Length * 2
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int](36 + $dataLength))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $writer.Write([int]16)
        $writer.Write([int16]1) # WAVE_FORMAT_PCM
        $writer.Write([int16]1) # mono
        $writer.Write([int]$SampleRate)
        $writer.Write([int]($SampleRate * 2))
        $writer.Write([int16]2)
        $writer.Write([int16]16)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]$dataLength)
        foreach ($sample in $Samples) {
            $writer.Write([int16]$sample)
        }
        $writer.Flush()
        $stream.Flush($true)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function New-DeterministicLectureWav {
    param([Parameter(Mandatory)] [string]$Path)

    $sampleRate = 16000
    $durationSeconds = 4
    $samples = [int16[]]::new($sampleRate * $durationSeconds)
    for ($index = 0; $index -lt $samples.Length; $index++) {
        $second = [Math]::Floor($index / $sampleRate)
        if ($second -eq 1 -or $second -eq 3) {
            $phase = 2 * [Math]::PI * 440 * $index / $sampleRate
            $samples[$index] = [Convert]::ToInt16([Math]::Round(12000 * [Math]::Sin($phase)))
        }
    }

    Write-Pcm16MonoWav -Path $Path -Samples $samples -SampleRate $sampleRate
}

function Read-Ascii {
    param(
        [Parameter(Mandatory)] [System.IO.BinaryReader]$Reader,
        [Parameter(Mandatory)] [int]$Count
    )

    $bytes = $Reader.ReadBytes($Count)
    if ($bytes.Length -ne $Count) {
        throw 'WAV header ended unexpectedly.'
    }
    return [System.Text.Encoding]::ASCII.GetString($bytes)
}

function Get-Pcm16WavInfo {
    param([Parameter(Mandatory)] [string]$Path)

    $item = Get-Item -LiteralPath $Path
    if ($item.Extension -ine '.wav') {
        throw "Input must use the .wav extension: $Path"
    }
    if ($item.Length -lt 44 -or $item.Length -gt 25MB) {
        throw 'WAV must be between 44 bytes and 25 MiB for this study.'
    }

    $stream = [System.IO.File]::OpenRead($item.FullName)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        if ((Read-Ascii -Reader $reader -Count 4) -ne 'RIFF') {
            throw 'Missing RIFF magic bytes.'
        }
        [void]$reader.ReadInt32()
        if ((Read-Ascii -Reader $reader -Count 4) -ne 'WAVE') {
            throw 'Missing WAVE magic bytes.'
        }

        $format = $null
        $channels = $null
        $sampleRate = $null
        $bitsPerSample = $null
        $dataOffset = $null
        $dataLength = $null

        while ($stream.Position + 8 -le $stream.Length) {
            $chunkId = Read-Ascii -Reader $reader -Count 4
            $chunkLength = $reader.ReadInt32()
            if ($chunkLength -lt 0 -or $stream.Position + $chunkLength -gt $stream.Length) {
                throw "Invalid WAV chunk length for '$chunkId'."
            }

            if ($chunkId -eq 'fmt ') {
                if ($chunkLength -lt 16) { throw 'The fmt chunk is too short.' }
                $format = $reader.ReadInt16()
                $channels = $reader.ReadInt16()
                $sampleRate = $reader.ReadInt32()
                [void]$reader.ReadInt32() # byte rate
                [void]$reader.ReadInt16() # block alignment
                $bitsPerSample = $reader.ReadInt16()
                $stream.Position += $chunkLength - 16
            }
            elseif ($chunkId -eq 'data') {
                $dataOffset = $stream.Position
                $dataLength = $chunkLength
                $stream.Position += $chunkLength
            }
            else {
                $stream.Position += $chunkLength
            }

            if (($chunkLength % 2) -eq 1 -and $stream.Position -lt $stream.Length) {
                $stream.Position++
            }
            if ($null -ne $format -and $null -ne $dataOffset) { break }
        }

        if ($format -ne 1 -or $channels -ne 1 -or $bitsPerSample -ne 16) {
            throw 'This sample accepts only uncompressed mono PCM16 WAV.'
        }
        if ($sampleRate -ne 16000) {
            throw 'This sample expects 16 kHz input, matching the intended ASR adapter contract.'
        }
        if ($null -eq $dataOffset -or $dataLength -le 0 -or ($dataLength % 2) -ne 0) {
            throw 'WAV data chunk is missing or invalid.'
        }

        return [pscustomobject]@{
            Path = $item.FullName
            SampleRate = [int]$sampleRate
            DataOffset = [long]$dataOffset
            DataLength = [int]$dataLength
            SampleCount = [int]($dataLength / 2)
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Read-Pcm16Samples {
    param([Parameter(Mandatory)] $WavInfo)

    $samples = [int16[]]::new($WavInfo.SampleCount)
    $stream = [System.IO.File]::OpenRead($WavInfo.Path)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        $stream.Position = $WavInfo.DataOffset
        for ($index = 0; $index -lt $samples.Length; $index++) {
            $samples[$index] = $reader.ReadInt16()
        }
        return ,$samples
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Measure-NormalizedRms {
    param([Parameter(Mandatory)] [int16[]]$Samples)

    if ($Samples.Length -eq 0) { return 0.0 }
    [double]$sumOfSquares = 0
    foreach ($sample in $Samples) {
        $value = [double]$sample
        $sumOfSquares += $value * $value
    }
    return [Math]::Sqrt($sumOfSquares / $Samples.Length) / 32768.0
}

$artifactRoot = Join-Path $PSScriptRoot '.artifacts'
$chunkRoot = Join-Path $artifactRoot 'chunks'
[System.IO.Directory]::CreateDirectory($chunkRoot) | Out-Null

if ([string]::IsNullOrWhiteSpace($InputPath)) {
    $resolvedInput = Join-Path $artifactRoot 'deterministic-lecture.wav'
    New-DeterministicLectureWav -Path $resolvedInput
    Write-Host "Generated deterministic WAV: $resolvedInput"
}
else {
    $resolvedInput = (Resolve-Path -LiteralPath $InputPath).Path
}

$wavInfo = Get-Pcm16WavInfo -Path $resolvedInput
$allSamples = Read-Pcm16Samples -WavInfo $wavInfo
$samplesPerChunk = $wavInfo.SampleRate * $ChunkSeconds
$chunkResults = [System.Collections.Generic.List[object]]::new()

for ($start = 0; $start -lt $allSamples.Length; $start += $samplesPerChunk) {
    $count = [Math]::Min($samplesPerChunk, $allSamples.Length - $start)
    $chunkSamples = [int16[]]::new($count)
    [Array]::Copy($allSamples, $start, $chunkSamples, 0, $count)
    $sequence = [int]($start / $samplesPerChunk)
    $chunkPath = Join-Path $chunkRoot ('chunk-{0:D3}.wav' -f $sequence)
    Write-Pcm16MonoWav -Path $chunkPath -Samples $chunkSamples -SampleRate $wavInfo.SampleRate

    $rms = Measure-NormalizedRms -Samples $chunkSamples
    $classification = if ($rms -ge $VadThreshold) { 'SPEECH' } else { 'SILENCE' }
    $chunkResults.Add([pscustomobject]@{
        sequence = $sequence
        durationSeconds = [Math]::Round($count / $wavInfo.SampleRate, 3)
        normalizedRms = [Math]::Round($rms, 6)
        threshold = $VadThreshold
        classification = $classification
        file = $chunkPath
    })
}

$summary = [pscustomobject]@{
    input = $resolvedInput
    contract = '16 kHz / mono / PCM16 WAV'
    sourceOfTruthOwner = 'local desktop/backend process'
    chunks = $chunkResults
}
$summaryPath = Join-Path $artifactRoot 'summary.json'
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$chunkResults | Format-Table sequence, durationSeconds, normalizedRms, classification -AutoSize
Write-Host "Summary: $summaryPath"
Write-Host 'This script generated/analyzed files only; it never opened the microphone.'
