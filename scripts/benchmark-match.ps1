<#
.SYNOPSIS
  Mede a latência do POST /match com consultas de 100, 1.000 e 10.000 hashes (Fase 2).

.DESCRIPTION
  Monta cada consulta amostrando hashes de backend/data/seed-hashes.txt (gerado por
  seed-fake-data.ps1), então o MGET encontra postings de verdade. Faz alguns requests de
  aquecimento e depois mede N requests por tamanho, reportando mediana e p95 de:
    - rtt: tempo total medido no cliente (inclui HTTP e serialização)
    - srv: processing_time_ms informado pela API (MGET + scoring + SQLite)
  A API precisa estar rodando (dotnet run --project backend/src/Shazamzin.Api).

.EXAMPLE
  ./scripts/benchmark-match.ps1
  ./scripts/benchmark-match.ps1 -Sizes 100,500 -Requests 50
#>
[CmdletBinding()]
param(
    [string]$Url = "http://localhost:5184/match",
    [string]$ApiKey = "dev-key-local",
    [int[]]$Sizes = @(100, 1000, 10000),
    [int]$Requests = 30,
    [int]$Warmup = 5
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$poolFile = Join-Path $root 'backend/data/seed-hashes.txt'
if (-not (Test-Path $poolFile)) { throw "Rode scripts/seed-fake-data.ps1 antes (falta $poolFile)." }
$pool = [System.IO.File]::ReadAllLines($poolFile) | ForEach-Object { [Convert]::ToUInt32($_, 16) }

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.DefaultRequestHeaders.Add('X-Api-Key', $ApiKey)
$client.DefaultRequestHeaders.Add('X-Device-ID', 'benchmark')

function New-Payload([int]$count) {
    $rng = New-Object System.Random(7)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([byte]1); $bw.Write([byte]0); $bw.Write([uint16]$count); $bw.Write([uint32]1)
    for ($i = 0; $i -lt $count; $i++) {
        $bw.Write([uint32]$pool[$rng.Next(0, $pool.Count)])
        $bw.Write([uint32]($i * 23))
    }
    $bw.Flush()
    ,$ms.ToArray()
}

function Get-Percentile([double[]]$values, [double]$p) {
    $sorted = $values | Sort-Object
    $sorted[[Math]::Min($sorted.Count - 1, [int][Math]::Ceiling($p * $sorted.Count) - 1)]
}

function Send-Once([byte[]]$bytes) {
    $content = New-Object System.Net.Http.ByteArrayContent(,$bytes)
    $content.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('application/octet-stream')
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = $client.PostAsync($Url, $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $sw.Stop()
    if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode): $body" }
    [pscustomobject]@{ Rtt = $sw.Elapsed.TotalMilliseconds; Srv = [double](($body | ConvertFrom-Json).processing_time_ms) }
}

$results = foreach ($size in $Sizes) {
    $bytes = New-Payload $size
    1..$Warmup | ForEach-Object { [void](Send-Once $bytes) }
    $samples = 1..$Requests | ForEach-Object { Send-Once $bytes }
    $rtt = $samples.Rtt; $srv = $samples.Srv
    [pscustomobject]@{
        Hashes     = $size
        'KB'       = [Math]::Round($bytes.Length / 1024, 1)
        'rtt p50'  = [Math]::Round((Get-Percentile $rtt 0.5), 1)
        'rtt p95'  = [Math]::Round((Get-Percentile $rtt 0.95), 1)
        'srv p50'  = [Math]::Round((Get-Percentile $srv 0.5), 1)
        'srv p95'  = [Math]::Round((Get-Percentile $srv 0.95), 1)
    }
}
Write-Host "`nLatencia em ms ($Requests requests por tamanho, $Warmup de aquecimento):"
$results | Format-Table -AutoSize
