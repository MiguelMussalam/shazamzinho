<#
.SYNOPSIS
  Gera uma playlist (.m3u) com músicas sorteadas do catálogo, para tocar perto do microfone.

.DESCRIPTION
  Lê o SQLite do catálogo e escreve uma playlist que abre em qualquer player (VLC, Windows Media
  Player, Groove). Toque uma faixa perto do microfone, aperte BOOT no ESP32 e confira título e
  artista no OLED/serial. Só entram músicas com pelo menos 60 s e cujo arquivo ainda existe.

.EXAMPLE
  ./scripts/gerar-playlist.ps1 -Count 20
#>
[CmdletBinding()]
param(
    [int]$Count = 20,
    [string]$Database,
    [string]$Output = 'C:\shazamzinho-dados\playlist-teste.m3u',
    [int]$Seed = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Database) { $Database = Join-Path $root 'backend/data/catalogo.db' }
$linhas = sqlite3 -separator "`t" $Database "SELECT id, title, artist, duration_sec, file_path FROM songs WHERE duration_sec >= 60;"
$musicas = $linhas | ForEach-Object { $c = $_ -split "`t"; [pscustomobject]@{ Id = $c[0]; Titulo = $c[1]; Artista = $c[2]; Dur = $c[3]; Arquivo = $c[4] } } |
    Where-Object { Test-Path -LiteralPath $_.Arquivo }
$rng = if ($Seed) { New-Object System.Random($Seed) } else { New-Object System.Random }
$escolhidas = $musicas | Sort-Object { $rng.Next() } | Select-Object -First $Count

$m3u = New-Object System.Collections.Generic.List[string]
$m3u.Add('#EXTM3U')
foreach ($m in $escolhidas) {
    $m3u.Add(('#EXTINF:{0},{1} - {2}' -f $m.Dur, $m.Artista, $m.Titulo))
    $m3u.Add($m.Arquivo)
}
New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
[IO.File]::WriteAllLines($Output, $m3u, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Playlist com $($escolhidas.Count) músicas: $Output"
$escolhidas | ForEach-Object { Write-Host ("  [{0}] {1} - {2}" -f $_.Id, $_.Artista, $_.Titulo) }