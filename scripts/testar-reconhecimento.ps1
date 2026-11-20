<#
.SYNOPSIS
  Mede a taxa de acerto do reconhecimento com trechos aleatórios de músicas do catálogo.

.DESCRIPTION
  Sorteia N músicas do SQLite do catálogo e, para cada uma, um trecho de -DurationSeconds em
  posição aleatória. O trecho é decodificado e passado pelo Olaf de novo (`extract`), como o ESP
  faria, e enviado ao POST /match. Conta acertos (mesmo song_id), erros (outra música) e
  não-reconhecidos, e mostra o tempo médio do servidor. A API precisa estar de pé
  (scripts/subir-servidor.ps1). Usa a chave de api-key.secret, se existir.

.EXAMPLE
  ./scripts/testar-reconhecimento.ps1 -Count 30 -DurationSeconds 3
#>
[CmdletBinding()]
param(
    [int]$Count = 30,
    [double]$DurationSeconds = 3,
    [string]$Database,
    [string]$Url = 'http://localhost:5184/match',
    [string]$ApiKey,
    [int]$Seed = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Database) { $Database = Join-Path $root 'backend/data/catalogo.db' }
if (-not $ApiKey) {
    $arq = Join-Path $root 'api-key.secret'
    $ApiKey = if (Test-Path $arq) { (Get-Content $arq -Raw).Trim() } else { 'dev-key-local' }
}
$exe = Join-Path $root 'backend/src/Shazamzin.Ingestion/bin/Debug/net10.0/Shazamzin.Ingestion.exe'
if (-not (Test-Path $exe)) { throw 'Compile a ingestão antes: dotnet build backend/src/Shazamzin.Ingestion' }

$linhas = sqlite3 -separator "`t" $Database "SELECT id, title, artist, duration_sec, file_path FROM songs WHERE duration_sec >= 30;"
$musicas = $linhas | ForEach-Object { $c = $_ -split "`t"; [pscustomobject]@{ Id = [int]$c[0]; Titulo = $c[1]; Artista = $c[2]; Dur = [double]$c[3]; Arquivo = $c[4] } }
$rng = if ($Seed) { New-Object System.Random($Seed) } else { New-Object System.Random }
$amostra = $musicas | Sort-Object { $rng.Next() } | Select-Object -First $Count

Add-Type -AssemblyName System.Net.Http
$http = New-Object System.Net.Http.HttpClient
$http.DefaultRequestHeaders.Add('X-Api-Key', $ApiKey)
$http.DefaultRequestHeaders.Add('X-Device-ID', 'teste-reconhecimento')
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('rec-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

$res = New-Object System.Collections.Generic.List[object]
try {
    $i = 0
    foreach ($m in $amostra) {
        $i++
        $offset = [math]::Round(5 + $rng.NextDouble() * [math]::Max(1, $m.Dur - $DurationSeconds - 10), 1)
        $bin = Join-Path $tmp "$i.bin"
        $ErrorActionPreference = 'Continue'
        $null = & $exe extract --path $m.Arquivo --offset-seconds $offset --duration-seconds $DurationSeconds --output $bin 2>&1
        $ErrorActionPreference = 'Stop'
        if (-not (Test-Path $bin)) { $res.Add([pscustomobject]@{ Id = $m.Id; Offset = $offset; Status = 'sem fingerprints'; Ms = 0 }); continue }
        $conteudoBin = New-Object System.Net.Http.ByteArrayContent(,[IO.File]::ReadAllBytes($bin))
        $conteudoBin.Headers.ContentType = 'application/octet-stream'
        $r = $http.PostAsync($Url, $conteudoBin).GetAwaiter().GetResult()
        $j = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $status = if (-not $j.match) { 'nao reconhecida' } elseif ($j.song_id -eq $m.Id) { 'acerto' } else { "ERRO (song $($j.song_id))" }
        $res.Add([pscustomobject]@{ Id = $m.Id; Offset = $offset; Status = $status; Ms = $j.processing_time_ms; Conf = $j.confidence })
        Write-Host ("[{0,2}/{1}] {2,-16} @{3,6}s  {4} - {5}" -f $i, $Count, $status, $offset, $m.Artista, $m.Titulo)
    }
} finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }

$acertos = @($res | Where-Object Status -eq 'acerto').Count
$erros = @($res | Where-Object Status -like 'ERRO*').Count
$semFp = @($res | Where-Object Status -eq 'sem fingerprints').Count
$nao = $res.Count - $acertos - $erros - $semFp
$validos = $res.Count - $semFp
Write-Host ""
Write-Host ("Trechos de {0}s: {1}/{2} acertos ({3:P0}), {4} nao reconhecidas, {5} erros (outra musica), {6} sem fingerprints (trecho invalido ou silencio). Servidor: {7:N0} ms em media." -f `
    $DurationSeconds, $acertos, $validos, ($acertos / [math]::Max(1, $validos)), $nao, $erros, $semFp, (($res | Where-Object Ms | Measure-Object Ms -Average).Average))