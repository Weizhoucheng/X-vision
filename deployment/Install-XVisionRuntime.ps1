[CmdletBinding()]
param(
    [ValidateSet('Check','InstallMissing')][string]$Action='Check',
    [string]$VisionMasterRoot='',
    [string]$OfficialVmInstaller='',
    [switch]$WithCamera,
    [string]$ReportPath=''
)
$ErrorActionPreference='Stop'
if(![Environment]::Is64BitProcess){throw 'Use a 64-bit PowerShell process for the x64 runtime.'}
if(!$ReportPath){$ReportPath=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('XVision\diagnostics\runtime-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')}
function Find-VmRoot {
    $candidates=@($VisionMasterRoot,$env:VISIONMASTER_ROOT,'D:\Program Files\VisionMaster4.3.0','C:\Program Files\VisionMaster4.3.0')
    foreach($candidate in $candidates){if($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'Development\V4.x\ComControls\Assembly\VM.Core.dll'))){return [IO.Path]::GetFullPath($candidate)}}
    return $null
}
function Invoke-OfficialInstaller([string]$Path,[string]$SignerPattern) {
    if(!(Test-Path -LiteralPath $Path -PathType Leaf)){throw "Installer not found: $Path"}
    $signature=Get-AuthenticodeSignature -LiteralPath $Path
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $SignerPattern){throw "Installer signature could not be verified. No installer was run: $Path"}
    if(@(Get-Process XVision,VisionMaster -ErrorAction SilentlyContinue).Count -gt 0){throw 'Close X Vision and VisionMaster before installing runtime components.'}
    $result=Start-Process -FilePath $Path -WorkingDirectory (Split-Path -Parent $Path) -Verb RunAs -WindowStyle Normal -Wait -PassThru
    if($result.ExitCode -notin @(0,3010)){throw "Official installer exited with code $($result.ExitCode)."}
    if($result.ExitCode -eq 3010){Write-Warning 'The official installer requires a Windows restart.'}
}
$vmRoot=Find-VmRoot
if(!$vmRoot -and $Action -eq 'InstallMissing'){
    if(!$OfficialVmInstaller){throw 'Provide -OfficialVmInstaller with the matching official VisionMaster 4.3.0 setup. This bundle does not issue or replace licenses.'}
    $media=Get-Item -LiteralPath $OfficialVmInstaller
    $identity=$media.Name+' '+$media.VersionInfo.ProductName+' '+$media.VersionInfo.ProductVersion
    if($identity -notmatch '(?i)VisionMaster.*4[._]3[._]0'){throw 'Cannot identify this as the matching VisionMaster 4.3.0 installation media. No installer was run.'}
    Invoke-OfficialInstaller $OfficialVmInstaller 'Hik|HaiKang|\u6d77\u5eb7'
    $vmRoot=Find-VmRoot
}
$netRelease=0
try{$netRelease=(Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release}catch{}
$sense=Get-Service -Name 'Sense Shield Service' -ErrorAction SilentlyContinue
$vcReady=$false
try{$vcReady=(Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64').Installed -eq 1}catch{}
if($Action -eq 'InstallMissing' -and $vmRoot){
    if($netRelease -lt 461808){throw '.NET Framework 4.7.2 or newer is required. Install the official Microsoft runtime first.'}
    if(!$vcReady){Invoke-OfficialInstaller (Join-Path $vmRoot 'Drivers\vc_redist_2015_x64.exe') 'Microsoft'}
    if(!$sense){Invoke-OfficialInstaller (Join-Path $vmRoot 'Drivers\SenseShield\sense_shield_installer_pub.exe') 'Hik|HaiKang|Sense|Deep|\u6df1\u76fe' }
    if($WithCamera){Invoke-OfficialInstaller (Join-Path $vmRoot 'Drivers\MVS_SDK_Setup.exe') 'Hik|HaiKang|\u6d77\u5eb7'}
}
$required=@('VM.Core.dll','VM.PlatformSDKCS.dll','ImageSourceModuleCs.dll','FormatModuleCs.dll')
$missing=@()
if($vmRoot){foreach($name in $required){if(!(Test-Path -LiteralPath (Join-Path $vmRoot ('Development\V4.x\ComControls\Assembly\'+$name)))){$missing+=$name}}}else{$missing+='Official VisionMaster runtime'}
$mode=$null;$activation=$null
if($vmRoot){
    $application=Join-Path $vmRoot 'Applications'
    if(!(Test-Path -LiteralPath $application)){$application=Join-Path $vmRoot 'Application'}
    $configuration=Join-Path $application 'Server\ServerAuthConfig.ini'
    if(Test-Path -LiteralPath $configuration){$match=[regex]::Match([IO.File]::ReadAllText($configuration),'(?mi)^\s*AuthorizeType\s*=\s*(\d+)\b');if($match.Success){$mode=[int]$match.Groups[1].Value}}
    $tool=Join-Path $application 'Tools\AuthActiveTool\SoftEncryptActive.exe';if(Test-Path -LiteralPath $tool){$activation=$tool}
}
$sense=Get-Service -Name 'Sense Shield Service' -ErrorAction SilentlyContinue
$report=[ordered]@{CheckedAt=(Get-Date).ToString('o');Action=$Action;VisionMasterRoot=$vmRoot;DotNetRelease=$netRelease;DotNetReady=($netRelease -ge 461808);Vc14X64Installed=$vcReady;MissingRuntimeFiles=@($missing);ConfiguredAuthorizationMode=$mode;SenseShieldService=$(if($sense){$sense.Status.ToString()}else{'NotInstalled'});ActivationTool=$activation;AuthorizationStatus='NotVerified';Note='Runtime/driver presence is not a license. Soft activation requires an official license covering this SDK and the algorithms.'}
$parent=Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath));if(!(Test-Path -LiteralPath $parent)){New-Item -ItemType Directory -Path $parent -Force | Out-Null}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$report | ConvertTo-Json -Depth 5
if($missing.Count -gt 0 -or $netRelease -lt 461808){exit 2}
exit 0
