param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$VisionMasterRoot = "C:\Program Files\VisionMaster4.3.0",
    [string]$MvsSdkDir = "C:\Program Files (x86)\MVDAlgorithmSDK\ReferencedAssemblies",
    [string]$OutputDirectory = ""
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
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "publish\XVision"
}

& (Join-Path $PSScriptRoot "Build.ps1") `
    -Configuration $Configuration `
    -VisionMasterRoot $VisionMasterRoot `
    -MvsSdkDir $MvsSdkDir `

if ($LASTEXITCODE -ne 0) { throw "构建失败" }

$source = Join-Path $root "src\VisionInspect.App\bin\$Configuration"
if (-not (Test-Path (Join-Path $source "XVision.exe"))) {
    throw "未找到构建输出: $source\XVision.exe"
}

# 发布目标可能由用户显式指定；绝不递归删除已有目录或覆盖生产配置。
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and
    @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) {
    throw "发布目录非空，请指定新的 -OutputDirectory：$OutputDirectory"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -eq 'XVision.exe' -or $_.Name -eq 'XVision.exe.config' -or $_.Extension -in @('.dll','.pdb','.xml') } | Copy-Item -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $root 'deployment') -Destination $OutputDirectory -Recurse
$logging=Join-Path $VisionMasterRoot 'Applications\3rdLib\Log4Net'
if(Test-Path -LiteralPath $logging){$destination=Join-Path $OutputDirectory '3rdLib\Log4Net';New-Item -ItemType Directory -Path $destination -Force | Out-Null;Get-ChildItem -LiteralPath $logging -File -Filter '*.config' | Copy-Item -Destination $destination}
Copy-Item -LiteralPath (Join-Path $root "docs") -Destination $OutputDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $root "samples") -Destination $OutputDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $OutputDirectory

$configDir = Join-Path $OutputDirectory "config"
New-Item -ItemType Directory -Force $configDir | Out-Null
Copy-Item (Join-Path $root "config\appsettings.example.json") (Join-Path $configDir "appsettings.example.json") -Force

$readme = @"
X Vision 发布包
====================

1. 目标机必须安装：
   - .NET Framework 4.7.2 或更高
   - VisionMaster 4.3.0 x64 + 有效许可
   - 海康 MVS 运行时/相机驱动（原生 MvCameraControl.dll）

2. 首次启动：
   程序默认在 %ProgramData%\VisionInspect\config 自动生成配置。
   也可先将 config\appsettings.example.json 复制到该目录为 appsettings.json，
   修改 VisionMasterInstallDir 与 Storage.DataRoot；若使用独立数据目录，
   运行 XVision.exe --data=D:\VisionInspectData，配置应位于该目录的 config 子目录。
   程序会从 VisionMaster 安装目录加载 VM 私有程序集。

3. 详见 docs\DEPLOYMENT.md 与 docs\TROUBLESHOOTING.md。
"@
$readme | Set-Content (Join-Path $OutputDirectory "DEPLOY_README.txt") -Encoding UTF8

# 为现场验收提供完整性清单。不会包含现场 .sol、用户库或密钥。
Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File |
    Where-Object { $_.Name -ne "SHA256.csv" } |
    ForEach-Object {
        [PSCustomObject]@{
            File = $_.FullName.Substring($OutputDirectory.TrimEnd('\').Length + 1)
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    } | Export-Csv -LiteralPath (Join-Path $OutputDirectory "SHA256.csv") -NoTypeInformation -Encoding UTF8

Write-Host "发布目录: $OutputDirectory" -ForegroundColor Green
Write-Host "目标机仍需匹配版本的官方 VM 4.3.0 运行环境及有效许可。" -ForegroundColor Yellow
