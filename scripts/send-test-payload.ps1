<#
.SYNOPSIS
  Gera um payload binário no formato do ESP (docs/protocol.md) e envia ao POST /match.

.DESCRIPTION
  Monta header (8 bytes) + N fingerprints (8 bytes cada), tudo little-endian, e faz o POST
  com X-Device-ID e X-Api-Key. Serve para testar a API sem o ESP32.

.PARAMETER Count
  Quantidade de fingerprints (default 180). Com o matching fake, >= 10 devolve match.

.EXAMPLE
  ./scripts/send-test-payload.ps1
  ./scripts/send-test-payload.ps1 -Count 3          # no-match
  ./scripts/send-test-payload.ps1 -ApiKey errada    # 401
#>
[CmdletBinding()]
param(
    [string]$Url = "http://localhost:5184/match",
    [string]$ApiKey = "dev-key-local",
    [string]$DeviceId = "esp32-ps",
    [ValidateRange(0, 65535)][int]$Count = 180,
    [uint32]$SessionId = 1,
    [string]$File   # envia um payload binário pronto (ex.: os de seed-fake-data.ps1) em vez de gerar um
)

$ErrorActionPreference = 'Stop'

if ($File) {
    $bytes = [System.IO.File]::ReadAllBytes($File)
    Write-Host "Payload: $($bytes.Length) bytes (de $File) -> $Url"
} else {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)   # BinaryWriter escreve little-endian
    $bw.Write([byte]1)             # protocol_version
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]$Count)      # count
    $bw.Write([uint32]$SessionId)  # session_id
    for ($i = 0; $i -lt $Count; $i++) {
        $bw.Write([uint32](0xA3F29B01L + $i)) # hash (sufixo L: sem ele o literal vira Int32 negativo)
        $bw.Write([uint32]($i * 23))          # timestamp_ms
    }
    $bw.Flush()
    $bytes = $ms.ToArray()
    Write-Host "Payload: $($bytes.Length) bytes ($Count fingerprints) -> $Url"
}

try {
    $response = Invoke-WebRequest -Uri $Url -Method Post -Body $bytes `
        -ContentType "application/octet-stream" `
        -Headers @{ "X-Device-ID" = $DeviceId; "X-Api-Key" = $ApiKey } `
        -UseBasicParsing
    Write-Host "HTTP $($response.StatusCode)"
    $response.Content
} catch {
    $r = $_.Exception.Response
    if ($r) { Write-Host "HTTP $([int]$r.StatusCode)" }
    Write-Host $_.ErrorDetails.Message
    exit 1
}
