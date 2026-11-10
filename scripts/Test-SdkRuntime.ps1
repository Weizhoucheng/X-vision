param([string]$SdkRoot='D:\Program Files\VisionMaster4.3.0',[switch]$ExpectAuthorizationUnavailable)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$run=Join-Path $root ('artifacts\sdk-runtime-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\XVision.Sdk\bin\Release\XVision.Sdk.dll'),(Join-Path $root 'src\VisionInspect.Core\bin\Release\VisionInspect.Core.dll'),(Join-Path $root 'src\VisionInspect.VisionMaster\bin\Release\VisionInspect.VisionMaster.dll') -Destination $run
$exe=Join-Path $run 'SdkRuntimeRegression.exe';$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /platform:x64 /target:exe ('/out:'+$exe) ('/reference:'+(Join-Path $run 'XVision.Sdk.dll')) ('/reference:'+(Join-Path $run 'VisionInspect.Core.dll')) ('/reference:'+(Join-Path $run 'VisionInspect.VisionMaster.dll')) /reference:System.Drawing.dll (Join-Path $root 'tests\SdkRuntimeRegression.cs')
if($LASTEXITCODE -ne 0){throw 'SDK consumer regression compile failed'}
$authorization=$(if($ExpectAuthorizationUnavailable){'unavailable'}else{'available'})
& $exe $SdkRoot $run $authorization 2>&1 | Tee-Object -FilePath (Join-Path $run 'verification.log')
if($LASTEXITCODE -ne 0){throw "SDK runtime regression failed: $run"}
