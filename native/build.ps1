<#
.SYNOPSIS
  Compila o extrator Olaf fixado em olaf.version e a CLI C de referência.
.EXAMPLE
  ./native/build.ps1
#>
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Config = 'Release')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$olafDir = Join-Path $here 'olaf'
$revision = (Get-Content (Join-Path $here 'olaf.version') -Raw).Trim()
foreach ($tool in 'git','cmake') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool não encontrado no PATH." }
}
if (-not (Test-Path -LiteralPath $olafDir)) {
    git clone https://github.com/JorenSix/Olaf.git $olafDir
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao clonar Olaf.' }
}
if (git -C $olafDir status --porcelain) { throw 'Olaf possui alterações locais; não é seguro identificar este build com a revisão fixada.' }
$currentRevision = git -C $olafDir rev-parse HEAD
if ($currentRevision -ne $revision) {
    if (git -C $olafDir status --porcelain) { throw 'Olaf possui alterações locais; não serão substituídas.' }
    git -C $olafDir fetch --depth 1 origin $revision
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao buscar a revisão fixada.' }
    git -C $olafDir checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao selecionar a revisão fixada.' }
}
$buildDir = Join-Path $here 'build'
$cmakeArgs = @('-S', $here, '-B', $buildDir, "-DCMAKE_BUILD_TYPE=$Config")
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if ($gcc) {
    $ninja = Get-Command ninja -ErrorAction SilentlyContinue
    $ninjaPath = if ($ninja) { $ninja.Source } else { Join-Path $env:USERPROFILE '.platformio\packages\tool-ninja\ninja.exe' }
    if (-not (Test-Path -LiteralPath $ninjaPath)) { throw 'Instale Ninja ou a toolchain do PlatformIO.' }
    $cmakeArgs += @('-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$ninjaPath", "-DCMAKE_C_COMPILER=$($gcc.Source)")
}
cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar CMake.' }
cmake --build $buildDir --config $Config
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar Olaf.' }
Write-Host "Build pronta em $buildDir. Rode dotnet build para copiar a biblioteca para os executáveis .NET."
