<#
.SYNOPSIS
  Ingere um arquivo de áudio ou uma pasta inteira (recursiva) no catálogo.

.DESCRIPTION
  Chama `Shazamzin.Ingestion add` para cada arquivo, em sequência (o escritor do catálogo
  é sequencial por SQLite). Uma falha num arquivo não interrompe os demais: o resumo final
  lista ok, pulados (já indexados) e falhas, e um CSV com o resultado de cada arquivo é
  gravado ao lado do banco SQLite. Repetir o comando é seguro: arquivos concluídos são pulados
  e ingestões incompletas são reparadas (ver docs/ingestion.md).

  Com -JamendoMeta, título/artista/álbum vêm do raw.meta.tsv do MTG-Jamendo (o ID da faixa
  é o número do nome do arquivo, ex.: 1002000.low.mp3 -> track_1002000).

  Requer FFmpeg/FFprobe e libolaf.dll (ver native/build.ps1).

.PARAMETER Path
  Arquivo de áudio ou pasta (busca .mp3, .flac, .wav, .ogg, .m4a, .opus recursivamente).

.PARAMETER Concurrency
  Reservado; somente 1 é aceito atualmente.

.PARAMETER Limit
  Opcional: ingere no máximo N arquivos (útil para testar).

.EXAMPLE
  ./scripts/ingest-folder.ps1 -Path "C:\Musicas\faixa.mp3" -Sqlite "Data Source=./data/catalogo.db" -Valkey "localhost:6379,defaultDatabase=1"

.EXAMPLE
  ./scripts/ingest-folder.ps1 -Path C:\shazamzinho-dados\jamendo\audio -JamendoMeta C:\shazamzinho-dados\jamendo\raw.meta.tsv `
    -Sqlite "Data Source=./data/catalogo.db" -Valkey "localhost:6379,defaultDatabase=1"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Concurrency = 1,
    [string]$Sqlite,
    [string]$Valkey,
    [string]$JamendoMeta,
    [int]$Limit = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Concurrency -ne 1) { throw 'Paralelismo ainda não implementado; use -Concurrency 1.' }

$exe = Join-Path $root 'backend/src/Shazamzin.Ingestion/bin/Debug/net10.0/Shazamzin.Ingestion.exe'
& dotnet build (Join-Path $root 'backend/src/Shazamzin.Ingestion') -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw 'dotnet build da ingestão falhou.' }

$extensoes = '.mp3', '.flac', '.wav', '.ogg', '.m4a', '.opus'
if (Test-Path -LiteralPath $Path -PathType Leaf) {
    $arquivos = @(Get-Item -LiteralPath $Path)
} elseif (Test-Path -LiteralPath $Path -PathType Container) {
    $arquivos = @(Get-ChildItem -LiteralPath $Path -Recurse -File |
        Where-Object { $extensoes -contains $_.Extension.ToLowerInvariant() } | Sort-Object FullName)
} else { throw "Caminho não encontrado: $Path" }
if ($Limit -gt 0) { $arquivos = @($arquivos | Select-Object -First $Limit) }
if ($arquivos.Count -eq 0) { throw "Nenhum arquivo de áudio em $Path." }

$meta = @{}
if ($JamendoMeta) {
    # O TSV não usa aspas para delimitar campos. Import-Csv as interpreta e perde linhas e aspas.
    $colunas = $null
    $malformadas = 0
    foreach ($linha in [IO.File]::ReadLines((Convert-Path -LiteralPath $JamendoMeta))) {
        $campos = $linha.Split("`t")
        if ($null -eq $colunas) { $colunas = $campos; continue }
        if ($campos.Count -ne $colunas.Count) { $malformadas++; continue }
        $registro = @{}
        for ($j = 0; $j -lt $campos.Count; $j++) { $registro[$colunas[$j]] = $campos[$j] }
        $meta[$registro.TRACK_ID] = $registro
    }
    if ($malformadas) { Write-Warning "$malformadas linhas do TSV com número errado de campos foram ignoradas." }
    Write-Host "Metadados Jamendo: $($meta.Count) faixas."
}

# Não usa '& exe': o PowerShell 5.1 perde ou divide argumentos com aspas (títulos e álbuns têm).
function Invoke-Programa([string]$Programa, [string[]]$Argumentos) {
    $partes = foreach ($a in $Argumentos) { '"' + ($a -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
    $psi = New-Object System.Diagnostics.ProcessStartInfo $Programa, ($partes -join ' ')
    $psi.WorkingDirectory = (Get-Location -PSProvider FileSystem).ProviderPath
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = [Diagnostics.Process]::Start($psi)
    try {
        $stderr = $proc.StandardError.ReadToEndAsync() # assíncrono para os dois pipes não travarem
        $stdout = $proc.StandardOutput.ReadToEnd()
        $proc.WaitForExit()
        return [pscustomobject]@{ Codigo = $proc.ExitCode; Saida = $stdout; Erro = $stderr.Result }
    } finally { $proc.Dispose() }
}

$resultados = New-Object System.Collections.Generic.List[object]
$inicio = Get-Date
for ($i = 0; $i -lt $arquivos.Count; $i++) {
    $arq = $arquivos[$i]
    $cli = @('add', '--path', $arq.FullName)
    if ($Sqlite) { $cli += @('--sqlite', $Sqlite) }
    if ($Valkey) { $cli += @('--valkey', $Valkey) }
    if ($meta.Count -and $arq.Name -match '^(\d+)') {
        $m = $meta['track_' + $Matches[1].PadLeft(7, '0')]
        if ($m) {
            if ($m.TRACK_NAME) { $cli += @('--title', $m.TRACK_NAME) }
            if ($m.ARTIST_NAME) { $cli += @('--artist', $m.ARTIST_NAME) }
            if ($m.ALBUM_NAME) { $cli += @('--album', $m.ALBUM_NAME) }
        }
    }

    $t0 = Get-Date
    $exec = Invoke-Programa $exe $cli
    $codigo = $exec.Codigo
    if ($codigo -ne 0) {
        # Uma nova tentativa: as falhas vistas na prática foram conexões momentâneas com o Valkey
        # (máquina ocupada). Repetir é seguro: a ingestão é idempotente e repara escritas parciais.
        Start-Sleep -Seconds 2
        $exec = Invoke-Programa $exe $cli
        $codigo = $exec.Codigo
    }
    $json = $exec.Saida
    $erro = ($exec.Erro -split '\r?\n' | Where-Object { $_ -match 'Erro|Error' }) -join ' '
    $r = [pscustomobject]@{ Arquivo = $arq.FullName; Status = 'falha'; SongId = ''; Fingerprints = ''; Segundos = 0; Erro = $erro }
    if ($codigo -eq 0) {
        try {
            $o = $json | ConvertFrom-Json
            $r.Status = if ($o.skipped) { 'pulado' } else { 'ok' }
            $r.SongId = $o.song_id; $r.Fingerprints = $o.fingerprints
        } catch { $r.Erro = "saída inesperada: $json" }
    } elseif (-not $r.Erro) { $r.Erro = "código $codigo" }
    $r.Segundos = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
    $resultados.Add($r)

    $decorrido = ((Get-Date) - $inicio).TotalSeconds
    $restante = [math]::Round($decorrido / ($i + 1) * ($arquivos.Count - $i - 1) / 60, 1)
    Write-Host ("[{0}/{1}] {2,-6} {3}  ({4}s, ~{5} min restantes) {6}" -f ($i + 1), $arquivos.Count, $r.Status,
        $arq.Name, $r.Segundos, $restante, $r.Erro)
}

$ok = @($resultados | Where-Object Status -eq 'ok').Count
$pulados = @($resultados | Where-Object Status -eq 'pulado').Count
$falhas = @($resultados | Where-Object Status -eq 'falha')
$csv = Join-Path $root ('backend/data/ingestao-{0:yyyyMMdd-HHmmss}.csv' -f (Get-Date))
New-Item -ItemType Directory -Force -Path (Split-Path $csv) | Out-Null
$resultados | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
Write-Host ""
Write-Host ("Resumo: {0} ok, {1} pulados, {2} falhas em {3:N1} min. Detalhes: {4}" -f $ok, $pulados, $falhas.Count,
    ((Get-Date) - $inicio).TotalMinutes, $csv)
if ($falhas.Count) { exit 1 }