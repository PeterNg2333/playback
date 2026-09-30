$ErrorActionPreference = 'Stop'
$fixtures = Join-Path $PSScriptRoot '../data/validation/fixtures'
[IO.Directory]::CreateDirectory($fixtures) | Out-Null
$cases = @(
  @{ id='cantonese'; voice='MSTTS_V110_zhHK_TracyM'; reference='佢哋同我哋唔係喺呢度上堂，而家我哋講緩存，記住保留廣東話口語。' },
  @{ id='mixed'; voice='MSTTS_V110_zhHK_TracyM'; reference='我哋而家講 cache。Cache stores frequently used data，唔係每次都讀 database。呢度用 FFT 同 spectrogram 分析聲音。' },
  @{ id='english'; voice='MSTTS_V110_enUS_ZiraM'; reference='A cache stores frequently used data to reduce latency. An FFT computes the discrete Fourier transform. A spectrogram shows frequency content over time.' }
)
foreach ($case in $cases) {
  $file = Join-Path $fixtures ($case.id + '.wav')
  if (!(Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -lt 1000) {
    $voice = New-Object -ComObject SAPI.SpVoice
    $token = New-Object -ComObject SAPI.SpObjectToken
    $token.SetId('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens\' + $case.voice)
    $voice.Voice = $token
    $stream = New-Object -ComObject SAPI.SpFileStream
    try {
      $stream.Open([IO.Path]::GetFullPath($file),3,$false)
      $voice.AudioOutputStream = $stream
      $voice.Speak($case.reference) | Out-Null
    } finally { $stream.Close() }
  }
  $case.audioSha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
  $case.referenceKind = 'prescribed synthetic script; not a human-transcribed natural recording'
}
$cases | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fixtures 'synthetic.json') -Encoding utf8
Write-Output 'Prepared three offline speech fixtures; no uploads.'
