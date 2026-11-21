<#
.SYNOPSIS
  Gera firmware/main/clipe/clipe_teste.c: um trecho de música para o ESP32 processar sem microfone.

.DESCRIPTION
  Roda `Shazamzin.Ingestion extract` no trecho pedido, que decodifica com FFmpeg e passa o PCM
  pelo Olaf no PC. Com o PCM float32 exato dessa extração (--raw-output) gera um arquivo C com
  literais hexadecimais de float (bit a bit iguais, sem depender de alinhamento de arquivo
  embutido). Salva ao lado os fingerprints que o PC calculou para o mesmo PCM
  (clipe_teste.pc.bin, payload v1), usados por comparar-fingerprints-esp.ps1.

  A música precisa estar no catálogo para o ESP ser reconhecido de ponta a ponta.

.EXAMPLE
  ./scripts/gerar-clipe-teste.ps1 -Path "C:\shazamzinho-dados\jamendo\audio\00\1234.low.mp3" -OffsetSeconds 40
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [double]$OffsetSeconds = 30,
    [double]$DurationSeconds = 5,
    [string]$Label,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'firmware/main/clipe' }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Arquivo de áudio não encontrado: $Path" }
if ($DurationSeconds -le 0 -or $DurationSeconds -gt 8) { throw 'Use uma duração entre 0 e 8 s (o trecho vai para a flash do ESP).' }

$exe = Join-Path $root 'backend/src/Shazamzin.Ingestion/bin/Debug/net10.0/Shazamzin.Ingestion.exe'
if (-not (Test-Path $exe)) {
    & dotnet build (Join-Path $root 'backend/src/Shazamzin.Ingestion') -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build da ingestão falhou.' }
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("clipe-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $bin = Join-Path $tmp 'clipe.bin'
    $raw = Join-Path $tmp 'clipe.f32'
    $saida = & $exe extract --path $Path --offset-seconds $OffsetSeconds --duration-seconds $DurationSeconds `
        --output $bin --raw-output $raw
    if ($LASTEXITCODE -ne 0) { throw "extract falhou (código $LASTEXITCODE)." }
    $info = ($saida -join "`n") | ConvertFrom-Json

    $bytes = [IO.File]::ReadAllBytes($raw)
    $n = $bytes.Length / 4
    $sb = New-Object System.Text.StringBuilder ($n * 22)
    for ($i = 0; $i -lt $n; $i++) {
        $bits = [BitConverter]::ToUInt32($bytes, $i * 4)
        $sinal = if ($bits -band 0x80000000) { '-' } else { '' }
        $exp = ($bits -shr 23) -band 0xFF
        $man = $bits -band 0x7FFFFF
        if ($exp -eq 0xFF) { throw "Amostra $i não é finita." }
        $lit = if ($exp -eq 0 -and $man -eq 0) { "${sinal}0.0f" }
               elseif ($exp -eq 0) { '{0}0x0.{1:x6}p-126f' -f $sinal, ($man -shl 1) }     # subnormal
               else { '{0}0x1.{1:x6}p{2}f' -f $sinal, ($man -shl 1), ($exp - 127) }      # normal
        [void]$sb.Append($lit).Append(',')
        if (($i + 1) % 8 -eq 0) { [void]$sb.Append("`n") } else { [void]$sb.Append(' ') }
    }

    if (-not $Label) { $Label = [IO.Path]::GetFileName($Path) }
    $origem = ('{0} @ {1}s +{2}s | perfil {3} | PC: {4} fingerprints' -f $Label, $OffsetSeconds, $DurationSeconds,
               $info.profile, $info.fingerprints) -replace '[^\w .@+|:/=-]', '_'

    $c = @"
// GERADO por scripts/gerar-clipe-teste.ps1 — não edite à mão.
// $origem
// PCM float32 mono 8 kHz, bit a bit igual ao que o PC entregou ao Olaf.
#include "clipe_teste.h"

const char clipe_teste_origem[] = "$origem";
const size_t clipe_teste_amostras = $n;
const float clipe_teste[$n] = {
$($sb.ToString().TrimEnd())
};
"@
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $OutDir 'clipe_teste.c'), $c, (New-Object System.Text.UTF8Encoding $false))
    Copy-Item $bin (Join-Path $OutDir 'clipe_teste.pc.bin') -Force
    Write-Host "OK: $n amostras ($($n / 8000) s), PC gerou $($info.fingerprints) fingerprints."
    Write-Host "    $origem"
} finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
