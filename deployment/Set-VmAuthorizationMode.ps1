[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)][string]$VisionMasterRoot,
    [Parameter(Mandatory=$true)][ValidateSet('LocalDongle','Soft','Network')][string]$Mode,
    [switch]$OfficialLicenseConfirmed
)
$ErrorActionPreference='Stop'
if(!$OfficialLicenseConfirmed){throw 'Confirm that an official license for the requested mode is available. Changing this selector does not create, migrate, activate, or validate a license.'}
if(@(Get-Process XVision,VisionMaster,VisionMasterServer -ErrorAction SilentlyContinue).Count -gt 0){throw 'Close X Vision and VisionMaster before changing the authorization selector.'}
$root=[IO.Path]::GetFullPath($VisionMasterRoot)
$application=Join-Path $root 'Applications';if(!(Test-Path -LiteralPath $application)){$application=Join-Path $root 'Application'}
$prefix=$application.TrimEnd('\')+'\'
if(@(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)}).Count -gt 0){throw 'Close the official VM applications and runtime server before changing this configuration.'}
$file=Join-Path $application 'Server\ServerAuthConfig.ini'
if(!(Test-Path -LiteralPath $file)){throw 'The matching official authorization configuration was not found.'}
$text=[IO.File]::ReadAllText($file)
$pattern='(?mi)^(\s*AuthorizeType\s*=\s*)\d+(.*)$'
if([regex]::Matches($text,$pattern).Count -ne 1){throw 'Unknown authorization configuration format; no file was modified.'}
$value=@{LocalDongle=0;Soft=2;Network=3}[$Mode]
if($PSCmdlet.ShouldProcess($file,"Select $Mode (AuthorizeType=$value)")){
    $backup=$file+'.XVision-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+([guid]::NewGuid().ToString('N').Substring(0,8))+'.bak'
    Copy-Item -LiteralPath $file -Destination $backup
    $updated=[regex]::Replace($text,$pattern,[System.Text.RegularExpressions.MatchEvaluator]{param($match) $match.Groups[1].Value+$value+$match.Groups[2].Value})
    try{[IO.File]::WriteAllText($file,$updated,[Text.UTF8Encoding]::new($false))}catch{Copy-Item -LiteralPath $backup -Destination $file -Force;throw}
    Write-Output "Selector updated. Backup: $backup"
    Write-Output 'Restart the official VM runtime as directed by its documentation, then validate .sol loading and algorithm execution without a local USB dongle.'
}
