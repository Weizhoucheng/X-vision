param(
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
$failed = $false
function Pass([string]$text) { Write-Host "[ OK ] $text" -ForegroundColor Green }
function Warn([string]$text) { Write-Host "[WARN] $text" -ForegroundColor Yellow }
function Fail([string]$text) { Write-Host "[FAIL] $text" -ForegroundColor Red; $script:failed = $true }
function Info([string]$text) { Write-Host "[INFO] $text" -ForegroundColor Cyan }

Info "VisionInspect 环境检查"
Info ("OS: " + [Environment]::OSVersion.VersionString + " / 64-bit process: " + [Environment]::Is64BitProcess)

# .NET Framework 4.7.2
try {
    $release = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -ErrorAction Stop).Release
    if ($release -ge 461808) { Pass ".NET Framework Release=$release（含 4.7.2）" }
    else { Fail ".NET Framework Release=$release，低于 4.7.2（461808）" }
} catch { Fail "无法读取 .NET Framework 版本" }

# Visual Studio / MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($vsPath) {
        $msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $msbuild) { Pass "MSBuild: $msbuild" }
        else { Fail "MSBuild 不存在: $msbuild" }
    } else { Fail "未找到安装 MSBuild 的 Visual Studio 2022" }
} else { Warn "未找到 vswhere；如果使用 VS 自带 MSBuild 可忽略" }

# VisionMaster
Info "VisionMaster 目录: $VisionMasterRoot"
$asm = Join-Path $VisionMasterRoot "Development\V4.x\ComControls\Assembly"
if ((Test-Path (Join-Path $VisionMasterRoot "Applications")) -or (Test-Path (Join-Path $VisionMasterRoot "Application"))) {
    Pass "Application/Applications 目录存在"
} else {
    Fail "缺少 Application/Applications 目录（不是完整 VM 安装目录？）"
}

$vmFiles = @(
    "VM.Core.dll",
    "VM.PlatformSDKCS.dll",
    "VMControls.Winform.Release.dll",
    "ImageSourceModuleCs.dll",
    "FormatModuleCs.dll"
)
foreach ($f in $vmFiles) {
    if (Test-Path (Join-Path $asm $f)) { Pass "VM DLL: $f" }
    else { Fail "缺少 VM DLL: $(Join-Path $asm $f)" }
}

# MVS（兼容 ReferencedAssemblies\Algorithms+Common 与 AnyCpu 两种布局）
Info "MVS SDK 根目录: $MvsSdkDir"
$mvsCameraDir = if (Test-Path (Join-Path $MvsSdkDir "MvCameraControl.Net.dll")) { $MvsSdkDir } else { Join-Path $MvsSdkDir "Algorithms" }
$mvsCommonDir = if (Test-Path (Join-Path $MvsSdkDir "MVDCore.Net.dll")) { $MvsSdkDir } else { Join-Path $MvsSdkDir "Common" }

$mvsFiles = @(
    @{ Name = "MvCameraControl.Net.dll"; Dir = $mvsCameraDir },
    @{ Name = "MVDCore.Net.dll"; Dir = $mvsCommonDir },
    @{ Name = "MVDImage.Net.dll"; Dir = $mvsCommonDir }
)
foreach ($item in $mvsFiles) {
    $path = Join-Path $item.Dir $item.Name
    if (Test-Path $path) { Pass "MVS DLL: $path" }
    else { Fail "缺少 MVS DLL: $path" }
}

$nativeCandidates = @(@(
    $env:MVS_RUNTIME_DIR,
    "C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64",
    "C:\Program Files\MVS\Runtime\Win64_x64",
    $MvsSdkDir
) | Where-Object { $_ -and (Test-Path (Join-Path $_ "MvCameraControl.dll")) })

if ($nativeCandidates.Count -gt 0) {
    Pass "找到原生 MvCameraControl.dll: $($nativeCandidates[0])"
} else {
    Warn "未找到原生 MvCameraControl.dll；相机功能需要安装 MVS Runtime 或将其加入 PATH"
}

if ($failed) {
    Write-Host "`n检查未通过，请先修复 FAIL 项。" -ForegroundColor Red
    exit 1
}

Write-Host "`n环境检查通过。" -ForegroundColor Green
exit 0
