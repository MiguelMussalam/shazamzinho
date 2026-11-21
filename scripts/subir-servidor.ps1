<#
.SYNOPSIS
  Sobe o servidor completo: Valkey, API e (opcionalmente) o túnel público do Cloudflare.

.DESCRIPTION
  - Garante o container shazamzin-valkey de pé.
  - Sobe a API em modo Production (sem /docs) com uma chave de API aleatória guardada em
    api-key.secret na raiz do repo (ignorado pelo git; criado na primeira execução).
  - Com -Tunnel (padrão), abre um túnel rápido do Cloudflare e imprime a URL pública e a linha
    pronta para firmware/main/config.h. O túnel rápido muda de URL a cada execução.
  - Ctrl+C encerra a API e o túnel.

.PARAMETER Sqlite / Valkey
  Par do catálogo. Padrão: o catálogo real (backend/data/catalogo.db + database 1).

.EXAMPLE
  ./scripts/subir-servidor.ps1
  ./scripts/subir-servidor.ps1 -Tunnel:$false     # só LAN: http://<ip-do-notebook>:5184/match
#>
[CmdletBinding()]
param(
    [string]$Sqlite = 'Data Source=./data/catalogo.db',
    [string]$Valkey = 'localhost:6379,defaultDatabase=1',
    [switch]$Tunnel = $true,
    [int]$Port = 5184
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root 'backend/logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

# --- Valkey ---
$estado = docker ps -a --filter 'name=^/shazamzin-valkey$' --format '{{.State}}' 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Docker indisponível. Abra o Docker Desktop.' }
if (-not $estado) { & (Join-Path $PSScriptRoot 'setup-valkey.ps1') }
elseif ($estado -ne 'running') { docker start shazamzin-valkey | Out-Null }

# --- chave da API ---
$arqChave = Join-Path $root 'api-key.secret'
if (-not (Test-Path $arqChave)) {
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [IO.File]::WriteAllText($arqChave, (-join ($bytes | ForEach-Object { $_.ToString('x2') })))
}
$chave = (Get-Content $arqChave -Raw).Trim()

# --- API ---
& dotnet build (Join-Path $root 'backend/src/Shazamzin.Api') -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet build da API falhou.' }
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
$env:Auth__ApiKey = $chave
$env:Sqlite__ConnectionString = $Sqlite
$env:Valkey__ConnectionString = $Valkey
$api = Start-Process dotnet -ArgumentList @('run', '--no-build', '--no-launch-profile', '--project', (Join-Path $root 'backend/src/Shazamzin.Api')) `
    -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $logs 'api.out.log') -RedirectStandardError (Join-Path $logs 'api.err.log')

$tunel = $null
try {
    $saude = $null
    for ($i = 0; $i -lt 60 -and -not $saude; $i++) {
        Start-Sleep 1
        try { $saude = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 2 }
        catch {
            # 503 = API de pé, mas Valkey ou SQLite indisponível: mostra o motivo em vez de esperar à toa.
            $r = $_.Exception.Response
            if ($r -and [int]$r.StatusCode -eq 503) {
                $corpo = (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd() | ConvertFrom-Json
                Write-Host ("API de pé, mas degradada: valkey={0} sqlite={1}. Tentando de novo..." -f $corpo.valkey, $corpo.sqlite)
            }
        }
        if ($api.HasExited) { throw "A API encerrou. Veja $logs\api.out.log" }
    }
    if (-not $saude) { throw "A API não respondeu em 60 s. Veja $logs\api.out.log" }
    Write-Host ("API ok: valkey={0} sqlite={1} musicas={2}" -f $saude.valkey, $saude.sqlite, $saude.indexed_songs)

    $ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notlike 'vEthernet*' } |
        Select-Object -First 1).IPAddress
    Write-Host "LAN:   http://${ip}:$Port/match  (o firewall do Windows pode bloquear; prefira o túnel)"

    if ($Tunnel) {
        $cfLog = Join-Path $logs 'cloudflared.log'
        Remove-Item $cfLog -ErrorAction SilentlyContinue
        $tunel = Start-Process cloudflared -ArgumentList @('tunnel', '--no-autoupdate', '--url', "http://127.0.0.1:$Port") `
            -NoNewWindow -PassThru -RedirectStandardError $cfLog -RedirectStandardOutput (Join-Path $logs 'cloudflared.out.log')
        $url = $null
        for ($i = 0; $i -lt 60 -and -not $url; $i++) {
            Start-Sleep 1
            if (Test-Path $cfLog) {
                $m = Select-String -Path $cfLog -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' | Select-Object -First 1
                if ($m) { $url = $m.Matches[0].Value }
            }
        }
        if (-not $url) { throw "O túnel não informou a URL em 60 s. Veja $cfLog" }
        Write-Host "Túnel: $url"
        Write-Host ""
        Write-Host "Para firmware/main/config.h:"
        Write-Host "  #define SERVER_URL       `"$url/match`""
        Write-Host "  #define API_KEY          `"$chave`""
    }
    Write-Host ""
    Write-Host 'Servidor no ar. Ctrl+C para encerrar.'
    Wait-Process -Id $api.Id
} finally {
    if ($tunel -and -not $tunel.HasExited) { Stop-Process -Id $tunel.Id -Force -ErrorAction SilentlyContinue }
    if (-not $api.HasExited) { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue }
    Get-CimInstance Win32_Process -Filter "Name='Shazamzin.Api.exe'" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}