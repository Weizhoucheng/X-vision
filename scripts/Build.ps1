param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$VisionMasterRoot = "C:\Program Files\VisionMaster4.3.0",
    [string]$MvsSdkDir = "C:\Program Files (x86)\MVDAlgorithmSDK\ReferencedAssemblies"
)

if (-not (Test-Path $VisionMasterRoot)) {
    if ($env:VISIONMASTER_ROOT -and (Test-Path $env:VISIONMASTER_ROOT)) {
        $VisionMasterRoot = $env:VISIONMASTER_ROOT
    } elseif (Test-Path "D:\Program Files\VisionMaster4.3.0") {
        $VisionMasterRoot = "D:\Program Files\VisionMaster4.3.0"
    }
}
if (-not (Test-Path (Join-Path $MvsSdkDir "MvCameraControl.Net.dll")) -and
    -not (Test-Path (Join-Path $MvsSdkDir "Algorithms\MvCameraControl.Net.dll")) -and
    (Test-Path "C:\Program Files (x86)\MVDC\MVS\Development\DotNet\AnyCpu\MvCameraControl.Net.dll")) {
    $MvsSdkDir = "C:\Program Files (x86)\MVDC\MVS\Development\DotNet\AnyCpu"
}
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "VisionInspect.sln"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($vsPath) { $msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe" }
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command) { $msbuild = $command.Source }
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw "找不到 MSBuild.exe，请安装 Visual Studio 2022 的「.NET 桌面开发」工作负载。"
}

Write-Host "MSBuild       : $msbuild"
Write-Host "Solution      : $solution"
Write-Host "Configuration : $Configuration|x64"
Write-Host "VisionMaster  : $VisionMasterRoot"
Write-Host "MVS SDK       : $MvsSdkDir"

$msbuildArgs = @(
    $solution,
    "/t:Rebuild",
    "/m",
    "/nologo",
    "/v:minimal",
    "/p:Configuration=$Configuration",
    "/p:Platform=x64",
    "/p:VisionMasterRoot=$VisionMasterRoot",
    "/p:MvsSdkDir=$MvsSdkDir"
)

& $msbuild @msbuildArgs
if ($LASTEXITCODE -ne 0) {
    throw "构建失败，退出码 $LASTEXITCODE"
}

Write-Host "构建完成: $root\src\VisionInspect.App\bin\$Configuration\XVision.exe" -ForegroundColor Green
