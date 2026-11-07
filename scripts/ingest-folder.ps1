<#
.SYNOPSIS
  Wrapper amigável do Shazamzin.Ingestion para ingerir uma pasta de músicas (Fase 3).

.DESCRIPTION
  Esqueleto. A CLI real (System.CommandLine) é implementada na Fase 3
  (architecture.md seções 6.3 e 8). Requer ffmpeg no PATH e libolaf.dll compilada
  (ver native/build.ps1).

.PARAMETER Path
  Pasta recursiva (ou arquivo único) a ingerir.

.PARAMETER Concurrency
  Nº de arquivos processados em paralelo (default = nº de cores lógicos).

.EXAMPLE
  ./scripts/ingest-folder.ps1 -Path "C:\Musicas"
  ./scripts/ingest-folder.ps1 -Path "C:\Musicas" -Concurrency 8
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Concurrency = [Environment]::ProcessorCount
)

$ErrorActionPreference = 'Stop'
$ingestionProj = Join-Path $PSScriptRoot '../backend/src/Shazamzin.Ingestion'

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    Write-Warning "ffmpeg não encontrado no PATH — a decodificação de áudio vai falhar (Fase 3)."
}

Write-Host "Ingerindo '$Path' (concurrency=$Concurrency)..."
dotnet run --project $ingestionProj -- add --path $Path --concurrency $Concurrency
