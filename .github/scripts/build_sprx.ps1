$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
if (-not $env:SCE_ORBIS_SDK_DIR -or -not (Test-Path $env:SCE_ORBIS_SDK_DIR)) {
    throw 'SCE_ORBIS_SDK_DIR must point to the installed ORBIS SDK on the private build runner.'
}
$python = (Get-Command python -ErrorAction Stop).Source
$configuration = if ($env:BO3_SPRX_CONFIGURATION) { $env:BO3_SPRX_CONFIGURATION } else { 'Debug' }
if ($configuration -notin @('Debug', 'Release')) { throw 'Unknown SPRX build configuration.' }
$msbuild = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($msbuild) {
    $builder = $msbuild.Source
} else {
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw 'MSBuild and Visual Studio ORBIS integration are required.' }
    $builder = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
    if (-not $builder) { throw 'MSBuild could not be located.' }
}
$output = Join-Path $root 'build/sprx/'
New-Item -ItemType Directory -Force $output | Out-Null
Push-Location $root
try {
    & $builder 'SPRX/BO3-Customs.vcxproj' /m /t:Rebuild "/p:Configuration=$configuration" /p:Platform=ORBIS "/p:OutDir=$output" "/p:PythonCommand=`"$python`""
    if ($LASTEXITCODE -ne 0) { throw "SPRX compilation failed (exit $LASTEXITCODE)." }
    & $python '.github/scripts/build_support.py' sprx (Join-Path $output 'BO3-Customs.sprx') 'build/artifact-sprx'
    if ($LASTEXITCODE -ne 0) { throw 'SPRX packaging failed.' }
} finally {
    Pop-Location
}
