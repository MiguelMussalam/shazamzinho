<#
.SYNOPSIS
  Popula o catálogo com 3 músicas fake (SQLite) e fingerprints simulados (Valkey), e gera
  payloads de consulta com ruído para testar o matching sem o ESP32 (Fase 2).

.DESCRIPTION
  - Aplica o schema no SQLite (idempotente) e insere 3 músicas com file_hash 'seed-N'.
  - Para cada música gera HashesPerSong hashes pseudo-aleatórios determinísticos (Random com
    seed = id) e grava cada chave fp:{hash} com SET do blob completo -> rodar de novo não duplica.
  - Escreve em backend/data/seed-queries/*.bin payloads (formato docs/protocol.md) que simulam
    uma captura de ~10 s a partir de um ponto da música, com 20% dos hashes trocados por ruído.
  - Escreve backend/data/seed-hashes.txt (todos os hashes) para o benchmark-match.ps1.

  Usa o database 0 do Valkey (o mesmo da API). Requer sqlite3 no PATH e o container do Valkey de pé.

.EXAMPLE
  ./scripts/seed-fake-data.ps1
  ./scripts/send-test-payload.ps1 -File backend/data/seed-queries/song2-at-30s.bin
#>
[CmdletBinding()]
param(
    [string]$DbPath,
    [string]$SchemaPath,
    [string]$Container = "shazamzin-valkey",
    [int]$HashesPerSong = 1500,
    [int]$QueryHashes = 100,
    [double]$NoiseFraction = 0.2
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot nao vale dentro dos defaults do param() no PowerShell 5.1: calcula aqui.
$root = Split-Path -Parent $PSScriptRoot
if (-not $DbPath) { $DbPath = Join-Path $root 'backend/data/shazamzin.db' }
if (-not $SchemaPath) { $SchemaPath = Join-Path $root 'backend/src/Shazamzin.Infrastructure/Sqlite/schema.sql' }
foreach ($tool in 'sqlite3', 'docker') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool não encontrado no PATH." }
}

$songs = @(
    @{ Hash = 'seed-1'; Title = 'Redemption Song';    Artist = 'Bob Marley'; Album = 'Uprising';             Duration = 227 },
    @{ Hash = 'seed-2'; Title = 'Garota de Ipanema';  Artist = 'Tom Jobim';  Album = 'Getz/Gilberto';        Duration = 185 },
    @{ Hash = 'seed-3'; Title = 'Bohemian Rhapsody';  Artist = 'Queen';      Album = 'A Night at the Opera'; Duration = 354 }
)

# ---- SQLite ----
$dataDir = Split-Path -Parent $DbPath
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
Get-Content $SchemaPath -Raw | sqlite3 $DbPath | Out-Null
foreach ($s in $songs) {
    $sql = "INSERT OR IGNORE INTO songs (title, artist, album, duration_sec, file_path, file_hash) " +
           "VALUES ('$($s.Title.Replace("'", "''"))', '$($s.Artist.Replace("'", "''"))', '$($s.Album.Replace("'", "''"))', $($s.Duration), 'seed/$($s.Hash).mp3', '$($s.Hash)');"
    sqlite3 $DbPath $sql
    $s.Id = [int](sqlite3 $DbPath "SELECT id FROM songs WHERE file_hash = '$($s.Hash)';")
}
Write-Host "Musicas no SQLite:"; $songs | ForEach-Object { Write-Host ("  id={0}  {1} - {2}" -f $_.Id, $_.Artist, $_.Title) }

# ---- fingerprints simulados ----
function New-Hash([System.Random]$rng) { [uint32]($rng.Next(0, 65536) * 65536L + $rng.Next(0, 65536)) }
function Add-Int32LE([System.Collections.Generic.List[byte]]$list, [int]$value) { $list.AddRange([System.BitConverter]::GetBytes($value)) }

$blobs = @{}   # hash(uint32) -> List[byte] (postings concatenados)
$allHashes = New-Object System.Collections.Generic.List[uint32]
$perSong = @{} # id -> lista ordenada de @{Hash; OffsetMs}
foreach ($s in $songs) {
    $rng = New-Object System.Random($s.Id)
    $step = [int]($s.Duration * 1000 / $HashesPerSong)
    $list = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $HashesPerSong; $i++) {
        $h = New-Hash $rng
        $off = $i * $step
        if (-not $blobs.ContainsKey($h)) { $blobs[$h] = New-Object 'System.Collections.Generic.List[byte]' }
        Add-Int32LE $blobs[$h] $s.Id
        Add-Int32LE $blobs[$h] $off
        $allHashes.Add($h)
        [void]$list.Add(@{ Hash = $h; OffsetMs = $off })
    }
    $perSong[$s.Id] = @{ Step = $step; Postings = $list }
}

# ---- Valkey: um SET por chave (idempotente) ----
$lines = New-Object System.Collections.Generic.List[string]
foreach ($kv in $blobs.GetEnumerator()) {
    $escaped = -join ($kv.Value | ForEach-Object { '\x{0:x2}' -f $_ })
    $lines.Add(('SET fp:{0:x8} "{1}"' -f $kv.Key, $escaped))
}
Write-Host "Gravando $($lines.Count) chaves no Valkey (container $Container)..."
([string]::Join("`n", $lines)) | docker exec -i $Container valkey-cli | Out-Null
Write-Host ("DBSIZE: " + (docker exec $Container valkey-cli DBSIZE))

# ---- payloads de consulta ----
$queryDir = Join-Path $dataDir 'seed-queries'
New-Item -ItemType Directory -Force -Path $queryDir | Out-Null
function Write-Payload([string]$path, [uint32]$session, $fingerprints) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([byte]1); $bw.Write([byte]0); $bw.Write([uint16]$fingerprints.Count); $bw.Write([uint32]$session)
    foreach ($f in $fingerprints) { $bw.Write([uint32]$f.Hash); $bw.Write([uint32]$f.TimestampMs) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
}

$noiseRng = New-Object System.Random(12345)
$queries = @(@{ Song = 1; StartSec = 12 }, @{ Song = 2; StartSec = 30 }, @{ Song = 3; StartSec = 120 })
foreach ($q in $queries) {
    $song = $songs[$q.Song - 1]
    $info = $perSong[$song.Id]
    $startIndex = [int]($q.StartSec * 1000 / $info.Step)
    $fps = for ($j = 0; $j -lt $QueryHashes; $j++) {
        $p = $info.Postings[$startIndex + $j]
        $hash = if ($noiseRng.NextDouble() -lt $NoiseFraction) { New-Hash $noiseRng } else { $p.Hash }
        @{ Hash = $hash; TimestampMs = $j * $info.Step }
    }
    $file = Join-Path $queryDir ("song{0}-at-{1}s.bin" -f $song.Id, $q.StartSec)
    Write-Payload $file 1 $fps
    Write-Host ("Consulta: {0}  ({1} hashes, {2:P0} ruido, comeca em {3}s de '{4}')" -f $file, $QueryHashes, $NoiseFraction, $q.StartSec, $song.Title)
}
# consulta que nao existe no catalogo
$noise = for ($j = 0; $j -lt $QueryHashes; $j++) { @{ Hash = (New-Hash $noiseRng); TimestampMs = $j * 23 } }
Write-Payload (Join-Path $queryDir 'noise-only.bin') 1 $noise
Write-Host ("Consulta: {0}" -f (Join-Path $queryDir 'noise-only.bin'))

[System.IO.File]::WriteAllLines((Join-Path $dataDir 'seed-hashes.txt'), ($allHashes | ForEach-Object { '{0:x8}' -f $_ }))
Write-Host "OK."
