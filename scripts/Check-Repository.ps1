$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tracked = @(& git -C $root ls-files --cached)
if ($LASTEXITCODE -ne 0) { throw 'Run this check in a Git working tree.' }
$problems = New-Object 'System.Collections.Generic.List[string]'
foreach ($relative in $tracked) {
    if ($relative -match '(?i)(^|/)(bin|obj|artifacts|publish|Data|Logs|Reports|Backups|\.vs)/' -or
        $relative -match '(?i)\.(dll|exe|pdb|zip|7z|msi|sol|lic|license|c2d|d2c|c2v|v2c|pfx|pem|key|pdf|docx)$' -or
        $relative -match '(?i)(^|/)(users\.json|appsettings\.json|\.env)$') {
        $problems.Add("Excluded file: $relative")
        continue
    }
    $file = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    if ((Get-Item -LiteralPath $file).Length -gt 5MB) { $problems.Add("File exceeds 5 MB: $relative") }
    if ([IO.Path]::GetExtension($file) -in @('.png','.ico')) { continue }
    $content = [IO.File]::ReadAllText($file)
    # Only report filename and category, never print the credential value.
    $patterns = @('gh[pousr]_[A-Za-z0-9]{30,}', 'github_pat_[A-Za-z0-9_]{30,}', '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----', 'AKIA[0-9A-Z]{16}')
    foreach ($pattern in $patterns) {
        if ($content -match $pattern) { $problems.Add("Potential credential: $relative"); break }
    }
}
if ($problems.Count) { $problems | ForEach-Object { Write-Output $_ }; throw 'Repository content check failed.' }
Write-Output "PASS repository contents: $($tracked.Count) tracked files; no forbidden file or known credential pattern found. Demo passwords are documented in SECURITY.md."
