<#
.SYNOPSIS
  Sobe o Valkey 8 em container Docker com a config da seção 5.3 do architecture.md.

.DESCRIPTION
  - maxmemory 9gb, maxmemory-policy noeviction (rejeita writes em vez de apagar dados)
  - Persistência RDB: save 900 1 / save 300 100
  - Porta 6379 exposta no host
  - Volume nomeado 'shazamzin-valkey-data' para persistir o dump.rdb

  Idempotente: se o container já existe, só dá start.

.EXAMPLE
  ./scripts/setup-valkey.ps1
#>
[CmdletBinding()]
param(
    [string]$ContainerName = "shazamzin-valkey",
    [int]$Port = 6379,
    [string]$MaxMemory = "9gb"
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker não encontrado no PATH. Instale o Docker Desktop ou Podman."
}

$exists = docker ps -a --filter "name=^/$ContainerName$" --format "{{.Names}}"
if ($exists -eq $ContainerName) {
    Write-Host "Container '$ContainerName' já existe — dando start."
    docker start $ContainerName | Out-Null
} else {
    Write-Host "Criando container '$ContainerName' (valkey/valkey:8)..."
    docker run -d `
        --name $ContainerName `
        -p "$($Port):6379" `
        -v shazamzin-valkey-data:/data `
        valkey/valkey:8 `
        valkey-server `
            --maxmemory $MaxMemory `
            --maxmemory-policy noeviction `
            --save "900 1" `
            --save "300 100" `
            --appendonly no | Out-Null
}

Start-Sleep -Seconds 1
Write-Host "PING:"
docker exec $ContainerName valkey-cli PING
