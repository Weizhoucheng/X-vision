param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$SkipBuild){& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release;if($LASTEXITCODE -ne 0){throw 'Build failed'}}
$run=Join-Path $root ('artifacts\theme-overlay-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$source=Join-Path $root 'src\VisionInspect.App\bin\Release'
Copy-Item -LiteralPath (Join-Path $source 'XVision.exe'),(Join-Path $source 'VisionInspect.Core.dll') -Destination $run
$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$exe=Join-Path $run 'ThemeOverlayRegression.exe'
& $compiler /nologo /platform:x64 /target:exe ("/out:"+$exe) ("/reference:"+(Join-Path $run 'XVision.exe')) /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $root 'tests\ThemeOverlayRegression.cs')
if($LASTEXITCODE -ne 0){throw 'Overlay regression compile failed'}
& $exe $run 2>&1 | Tee-Object -FilePath (Join-Path $run 'verification.log')
if($LASTEXITCODE -ne 0){throw "Overlay regression failed: $run"}
