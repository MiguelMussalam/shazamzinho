<#
.SYNOPSIS
  Clona e compila a lib Olaf como libolaf.dll (Fase 3).

.DESCRIPTION
  1. Clona o Olaf em native/olaf/ (se ainda não existir)
  2. Configura e compila via CMake (precisa de cmake + compilador C no PATH)
  3. Copia a DLL gerada para o bin do Shazamzin.Ingestion

  NOTA: ainda é um esqueleto. O wrapper native/olaf_wrapper.c e os ajustes de fontes
  em native/CMakeLists.txt são escritos na Fase 3. cmake não está instalado nesta
  máquina ainda.

.EXAMPLE
  ./native/build.ps1
#>
[CmdletBinding()]
param(
    [string]$OlafRepo = "https://github.com/JorenSix/Olaf.git",
    [string]$Config   = "Release"
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

foreach ($tool in 'git','cmake') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool não encontrado no PATH. Instale antes de rodar este script."
    }
}

$olafDir = Join-Path $here 'olaf'
if (-not (Test-Path $olafDir)) {
    Write-Host "Clonando Olaf em $olafDir ..."
    git clone --depth 1 $OlafRepo $olafDir
} else {
    Write-Host "Olaf já clonado em $olafDir."
}

$buildDir = Join-Path $here 'build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "Configurando CMake..."
cmake -S $here -B $buildDir -DCMAKE_BUILD_TYPE=$Config

Write-Host "Compilando..."
cmake --build $buildDir --config $Config

# Copia a DLL para o bin do Ingestion (ajustar TFM conforme necessário)
$dll = Get-ChildItem -Path $buildDir -Recurse -Filter 'olaf.dll','libolaf.dll' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($dll) {
    $dest = Join-Path $here '../backend/src/Shazamzin.Ingestion/bin/Debug/net10.0'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll.FullName (Join-Path $dest 'libolaf.dll') -Force
    Write-Host "DLL copiada para $dest"
} else {
    Write-Warning "DLL não encontrada em $buildDir — verifique a saída do CMake."
}
