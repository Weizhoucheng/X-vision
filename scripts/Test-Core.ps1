param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $vsPath) { throw 'Visual Studio MSBuild is required.' }
$msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
$compiler = Join-Path $vsPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
$project = Join-Path $root 'src\VisionInspect.Infrastructure\VisionInspect.Infrastructure.csproj'
& $msbuild $project /t:Build /nologo /v:minimal /p:Platform=x64 "/p:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) { throw 'Core/Infrastructure build failed.' }

# Run an x64 .NET Framework executable against the real service assemblies; VM/MVS hardware is not used.
$runDir = Join-Path ([System.IO.Path]::GetTempPath()) ('VisionInspect-CoreTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDir | Out-Null
$coreDll = Join-Path $root "src\VisionInspect.Core\bin\$Configuration\VisionInspect.Core.dll"
$infraDll = Join-Path $root "src\VisionInspect.Infrastructure\bin\$Configuration\VisionInspect.Infrastructure.dll"
Copy-Item -LiteralPath $coreDll -Destination $runDir
Copy-Item -LiteralPath $infraDll -Destination $runDir
$testExe = Join-Path $runDir 'CoreRegression.exe'
$testSource = Join-Path $root 'tests\CoreRegression.cs'
& $compiler /nologo /langversion:7.3 /platform:x64 /target:exe "/out:$testExe" "/reference:$coreDll" "/reference:$infraDll" /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $testSource
if ($LASTEXITCODE -ne 0) { throw 'Regression harness compilation failed.' }
& $testExe $runDir
if ($LASTEXITCODE -ne 0) { throw "Core regression failed; artifacts retained at $runDir" }
Write-Host "Core regressions passed. Artifacts: $runDir" -ForegroundColor Green
