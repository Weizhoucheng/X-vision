param([string]$SdkRoot='D:\Program Files\VisionMaster4.3.0')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$run=Join-Path $root ('artifacts\runtime-settings-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$source=Join-Path $root 'src\VisionInspect.App\bin\Release'
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $run
$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$refs=@('XVision.exe','VisionInspect.Core.dll','VisionInspect.Infrastructure.dll','VisionInspect.VisionMaster.dll','VisionInspect.Cameras.dll','VisionInspect.Communication.dll') | ForEach-Object { '/reference:'+(Join-Path $run $_) }
$exe=Join-Path $run 'RuntimeCommunicationRegression.exe'
& $compiler /nologo /platform:x64 /target:exe ("/out:"+$exe) ('/win32manifest:'+(Join-Path $root 'src\VisionInspect.App\app.manifest')) @refs /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $root 'tests\RuntimeCommunicationRegression.cs')
if($LASTEXITCODE -ne 0){throw 'Runtime test compile failed'}
Copy-Item -LiteralPath (Join-Path $source 'XVision.exe.config') -Destination ($exe+'.config')
& $exe $SdkRoot $run | Tee-Object -FilePath (Join-Path $run 'verification.log')
if($LASTEXITCODE -ne 0){throw "Runtime verification failed: $run"}
