param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release
if($LASTEXITCODE -ne 0){throw 'Build failed'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts\XVision-SDK-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0){throw 'Use a new empty output directory.'}
$lib=Join-Path $OutputDirectory 'lib\net472';$sample=Join-Path $OutputDirectory 'sample';$setup=Join-Path $OutputDirectory 'deployment'
New-Item -ItemType Directory -Path $lib,$sample,$setup -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\XVision.Sdk\bin\Release\XVision.Sdk.dll'),(Join-Path $root 'src\VisionInspect.Core\bin\Release\VisionInspect.Core.dll'),(Join-Path $root 'src\VisionInspect.VisionMaster\bin\Release\VisionInspect.VisionMaster.dll') -Destination $lib
Copy-Item -LiteralPath (Join-Path $root 'samples\XVisionSdkSample.cs') -Destination $sample
Get-ChildItem -LiteralPath $lib -File | Copy-Item -Destination $sample
$compiler='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /platform:x64 /target:exe ('/out:'+(Join-Path $sample 'XVisionSdkSample.exe')) ('/reference:'+(Join-Path $lib 'XVision.Sdk.dll')) ('/reference:'+(Join-Path $lib 'VisionInspect.Core.dll')) /reference:System.Drawing.dll (Join-Path $sample 'XVisionSdkSample.cs')
if($LASTEXITCODE -ne 0){throw 'SDK consumer sample failed to compile'}
Get-ChildItem -LiteralPath (Join-Path $root 'deployment') -File | Copy-Item -Destination $setup
Copy-Item -LiteralPath (Join-Path $root 'docs\SDK_RUNTIME_DEPLOYMENT.md') -Destination (Join-Path $OutputDirectory 'README.md')
@(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Where-Object Name -ne 'SHA256.csv') | ForEach-Object {[PSCustomObject]@{File=$_.FullName.Substring($OutputDirectory.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}} | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'SHA256.csv') -NoTypeInformation -Encoding UTF8
$zip=$OutputDirectory+'.zip';Compress-Archive -LiteralPath $OutputDirectory -DestinationPath $zip
Write-Output "SDK directory: $OutputDirectory"
Write-Output "SDK archive: $zip"
