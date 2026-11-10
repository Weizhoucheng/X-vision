param([string]$SdkRoot='D:\Program Files\VisionMaster4.3.0')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$run=Join-Path $root ('artifacts\ui-redesign-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$source=Join-Path $root 'src\VisionInspect.App\bin\Release'
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $run
$logConfig=Join-Path $SdkRoot 'Applications\3rdLib\Log4Net'
if(Test-Path -LiteralPath $logConfig){
    $destination=Join-Path $run '3rdLib\Log4Net'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $logConfig -Filter '*.config' -File | Copy-Item -Destination $destination
}
$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$refs=@('XVision.exe','VisionInspect.Core.dll','VisionInspect.Infrastructure.dll','VisionInspect.VisionMaster.dll','VisionInspect.Cameras.dll','VisionInspect.Communication.dll') | ForEach-Object { '/reference:'+(Join-Path $run $_) }
$exe=Join-Path $run 'UiRedesignSmoke.exe'
& $compiler /nologo /platform:x64 /langversion:7.3 /target:exe ("/out:"+$exe) @refs /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $root 'tests\UiRedesignSmoke.cs')
if($LASTEXITCODE -ne 0){throw 'UI harness compile failed'}
Copy-Item -LiteralPath (Join-Path $source 'XVision.exe.config') -Destination ($exe+'.config')
& $exe $SdkRoot $run | Tee-Object -FilePath (Join-Path $run 'verification.log')
if($LASTEXITCODE -ne 0){throw "UI verification failed: $run"}
