<#
.SYNOPSIS
  Wrapper inicial do Shazamzin.Ingestion para um arquivo de áudio (Fase 3).

.DESCRIPTION
  Nesta entrega aceita um arquivo e execução sequencial. Pastas e paralelismo são
  a próxima etapa. Requer FFmpeg/FFprobe e libolaf.dll (ver native/build.ps1).

.PARAMETER Path
  Arquivo de áudio a ingerir.

.PARAMETER Concurrency
  Reservado para a próxima entrega; somente 1 é aceito atualmente.

.EXAMPLE
  ./scripts/ingest-folder.ps1 -Path "C:\Musicas\faixa.mp3" -Sqlite "Data Source=./data/catalog-real.db" -Valkey "localhost:6379,defaultDatabase=1"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Concurrency = 1,
    [string]$Sqlite,
    [string]$Valkey
)

$ErrorActionPreference = 'Stop'
$ingestionProj = Join-Path $PSScriptRoot '../backend/src/Shazamzin.Ingestion'

if ($Concurrency -ne 1) { throw 'Paralelismo ainda não implementado; use -Concurrency 1.' }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Informe um arquivo de áudio. Ingestão de pastas ainda não implementada.' }
$cliArguments = @('run', '--project', $ingestionProj, '--', 'add', '--path', $Path)
if ($Sqlite) { $cliArguments += @('--sqlite', $Sqlite) }
if ($Valkey) { $cliArguments += @('--valkey', $Valkey) }
& dotnet @cliArguments
if ($LASTEXITCODE -ne 0) { throw "Ingestão falhou (código $LASTEXITCODE)." }
