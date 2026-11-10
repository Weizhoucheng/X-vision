$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$run=Join-Path $root ('artifacts\deployment-tools-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$fixture=Join-Path $run 'fake-vm\Applications\Server';New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$config=Join-Path $fixture 'ServerAuthConfig.ini'
$initial="# Fixture only`r`nAuthorizeType = 0`r`nKeepField=9`r`n";[IO.File]::WriteAllText($config,$initial)
$vm=Split-Path -Parent (Split-Path -Parent $fixture)
$script=Join-Path $root 'deployment\Set-VmAuthorizationMode.ps1'
$real='D:\Program Files\VisionMaster4.3.0\Applications\Server\ServerAuthConfig.ini';$realHash=(Get-FileHash -LiteralPath $real).Hash
$rejected=$false;try{& $script -VisionMasterRoot $vm -Mode Soft}catch{$rejected=$true}
if(!$rejected -or [IO.File]::ReadAllText($config) -ne $initial){throw 'Unconfirmed selector update must not modify files'}
& $script -VisionMasterRoot $vm -Mode Soft -OfficialLicenseConfirmed -WhatIf
if([IO.File]::ReadAllText($config) -ne $initial){throw 'WhatIf modified the file'}
& $script -VisionMasterRoot $vm -Mode Soft -OfficialLicenseConfirmed
$updated=[IO.File]::ReadAllText($config);if($updated -notmatch 'AuthorizeType = 2' -or $updated -notmatch 'KeepField=9'){throw 'Mode update failed to preserve other settings'}
if(@(Get-ChildItem -LiteralPath $fixture -File -Filter '*.bak').Count -ne 1){throw 'Original configuration backup missing'}
[IO.File]::WriteAllText($config,"AuthorizeType=0`r`nAuthorizeType=1`r`n")
$rejected=$false;try{& $script -VisionMasterRoot $vm -Mode Soft -OfficialLicenseConfirmed}catch{$rejected=$true}
if(!$rejected){throw 'Ambiguous selector file was accepted'}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $root 'deployment') -Filter '*.ps1'){$tokens=$null;$errors=$null;[System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count -gt 0){throw "PowerShell syntax errors: $($file.Name)"}}
if((Get-FileHash -LiteralPath $real).Hash -ne $realHash){throw 'Real VM configuration was changed by tests'}
'PASS selector confirmation guard / WhatIf / unique backup / preserved fields / malformed-file rejection / script parsing; real VM config unchanged' | Tee-Object -FilePath (Join-Path $run 'verification.log')
