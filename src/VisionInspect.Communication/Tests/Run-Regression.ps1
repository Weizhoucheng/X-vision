param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $vs) { throw 'Visual Studio / MSBuild not found.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
& $msbuild (Join-Path $repository 'src\VisionInspect.Communication\VisionInspect.Communication.csproj') /t:Build /p:Platform=x64 "/p:Configuration=$Configuration" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Communication project build failed.' }
$csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
$testOutput = Join-Path ([IO.Path]::GetTempPath()) ('VisionInspect-Devices-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $repository "src\VisionInspect.Communication\bin\$Configuration\VisionInspect.Communication.dll") -Destination $testOutput
Copy-Item -LiteralPath (Join-Path $repository "src\VisionInspect.Core\bin\$Configuration\VisionInspect.Core.dll") -Destination $testOutput
$exe = Join-Path $testOutput 'DeviceRegression.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$exe" "/reference:$(Join-Path $testOutput 'VisionInspect.Communication.dll')" "/reference:$(Join-Path $testOutput 'VisionInspect.Core.dll')" (Join-Path $PSScriptRoot 'DeviceRegression.cs.txt')
if ($LASTEXITCODE -ne 0) { throw 'Regression harness compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Device communication regression failed.' }
Write-Host "Harness output: $testOutput"
