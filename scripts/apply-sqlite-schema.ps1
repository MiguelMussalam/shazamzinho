<#
.SYNOPSIS
  Aplica o schema do SQLite (seção 5.4 do architecture.md) ao banco do backend.

.DESCRIPTION
  Cria backend/data/shazamzin.db (se não existir) e aplica o schema.sql.
  Idempotente: usa CREATE TABLE IF NOT EXISTS, então pode rodar quantas vezes quiser.
  Requer o sqlite3 CLI no PATH.

.EXAMPLE
  ./scripts/apply-sqlite-schema.ps1
#>
[CmdletBinding()]
param(
    [string]$DbPath = "$PSScriptRoot/../backend/data/shazamzin.db",
    [string]$SchemaPath = "$PSScriptRoot/../backend/src/Shazamzin.Infrastructure/Sqlite/schema.sql"
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlite3 -ErrorAction SilentlyContinue)) {
    throw "sqlite3 não encontrado no PATH. Instale o sqlite3 CLI ou use o pacote Microsoft.Data.Sqlite."
}

$dataDir = Split-Path -Parent $DbPath
if (-not (Test-Path $dataDir)) {
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
}

Write-Host "Aplicando schema em $DbPath ..."
Get-Content $SchemaPath -Raw | sqlite3 $DbPath
Write-Host "Tabelas criadas:"
sqlite3 $DbPath ".tables"
Write-Host "OK."
