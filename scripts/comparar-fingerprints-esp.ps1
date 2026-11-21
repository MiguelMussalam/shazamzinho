<#
.SYNOPSIS
  Compara os fingerprints que o ESP32 gera do clipe de teste com os que o PC gerou do mesmo PCM.

.DESCRIPTION
  Reseta o ESP pela serial (RTS), lê o bloco FP_INICIO..FP_FIM que o firmware imprime e compara
  com firmware/main/clipe/clipe_teste.pc.bin (payload v1 gerado por gerar-clipe-teste.ps1).
  Critério do marco M2 (docs/plano-finalizacao.md): >= 90% dos fingerprints do PC presentes no ESP
  com o mesmo hash e o mesmo timestamp. Também mostra a linha RESULTADO, se o ESP consultou a API.
  Feche o monitor serial do PlatformIO antes (a porta só pode ter um dono).

.EXAMPLE
  ./scripts/comparar-fingerprints-esp.ps1 -Port COM8
#>
[CmdletBinding()]
param(
    [string]$Port = 'COM8',
    [int]$TimeoutSeconds = 120,
    [string]$Reference,
    [string]$LogFile,
    [string]$FromLog   # reanalisa um log salvo por -LogFile, sem tocar na placa
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Reference) { $Reference = Join-Path $root 'firmware/main/clipe/clipe_teste.pc.bin' }

# --- referência do PC (payload v1: header 8 bytes + N x (u32 hash, u32 ms)) ---
$ref = [IO.File]::ReadAllBytes($Reference)
$n = [BitConverter]::ToUInt16($ref, 2)
$pc = for ($i = 0; $i -lt $n; $i++) {
    $o = 8 + 8 * $i
    '{0:x8} {1}' -f [BitConverter]::ToUInt32($ref, $o), [BitConverter]::ToUInt32($ref, $o + 4)
}

# --- captura serial (ou log salvo) ---
if ($FromLog) {
    $linhas = [Collections.Generic.List[string]]([IO.File]::ReadAllLines($FromLog))
} else {
$sp = New-Object System.IO.Ports.SerialPort $Port, 115200
$sp.ReadTimeout = 500
$sp.Open()
try {
    $sp.DtrEnable = $false; $sp.RtsEnable = $true; Start-Sleep -Milliseconds 150; $sp.RtsEnable = $false
    $linhas = New-Object System.Collections.Generic.List[string]
    $fim = (Get-Date).AddSeconds($TimeoutSeconds)
    $viuFim = $false
    while ((Get-Date) -lt $fim) {
        try { $l = $sp.ReadLine().TrimEnd("`r") } catch [TimeoutException] { continue }
        $linhas.Add($l)
        if ($l -match 'RESULTADO match=|HTTP -?\d+ em|sem Wi-Fi: ciclo encerrado|JSON invalido|sem memoria') { break }
        if ($l -eq 'FP_FIM') { $viuFim = $true; $fim = [datetime]::Now.AddSeconds(30) } # espera a consulta
    }
} finally { $sp.Close() }
}

if ($LogFile) { [IO.File]::WriteAllLines($LogFile, $linhas) }
$ini = $linhas.IndexOf(($linhas | Where-Object { $_ -like 'FP_INICIO*' } | Select-Object -First 1))
$fimIdx = $linhas.IndexOf('FP_FIM')
if ($ini -lt 0 -or $fimIdx -lt 0) {
    $linhas | Select-Object -Last 25 | ForEach-Object { Write-Host "  | $_" }
    throw "Bloco FP_INICIO..FP_FIM não apareceu em $TimeoutSeconds s."
}
$esp = $linhas[($ini + 1)..($fimIdx - 1)] | Where-Object { $_ -like 'FP *' } | ForEach-Object { $_.Substring(3) }

# --- comparação ---
$setEsp = New-Object 'System.Collections.Generic.HashSet[string]' (,[string[]]$esp)
$setPc = New-Object 'System.Collections.Generic.HashSet[string]' (,[string[]]$pc)
$iguais = @($pc | Where-Object { $setEsp.Contains($_) }).Count
$soEsp = @($esp | Where-Object { -not $setPc.Contains($_) }).Count
$hashesEsp = New-Object 'System.Collections.Generic.HashSet[string]' (,[string[]]($esp | ForEach-Object { $_.Split(' ')[0] }))
$mesmoHash = @($pc | Where-Object { $hashesEsp.Contains($_.Split(' ')[0]) }).Count
$pct = if ($pc.Count) { 100.0 * $iguais / $pc.Count } else { 0 }

Write-Host ""
Write-Host ($linhas[$ini])
Write-Host ("PC:  {0} fingerprints | ESP: {1}" -f $pc.Count, $esp.Count)
Write-Host ("Iguais (hash + tempo): {0} = {1:N1}% dos do PC" -f $iguais, $pct)
Write-Host ("Mesmo hash, qualquer tempo: {0} | So no ESP: {1}" -f $mesmoHash, $soEsp)
$linhas | Where-Object { $_ -match 'Olaf: |heap |RESULTADO match|IP obtido|HTTP -?\d+ em|sem Wi-Fi' } | ForEach-Object { Write-Host "  | $_" }
if ($pct -ge 90) { Write-Host "M2: APROVADO (>= 90%)" -ForegroundColor Green }
else { Write-Host "M2: REPROVADO (< 90%)" -ForegroundColor Red; exit 2 }
