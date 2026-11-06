$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ApplicationTransaction.ps1')
$root=Join-Path (Split-Path $PSScriptRoot -Parent) ('docs\evidence\v070\transaction-'+[Guid]::NewGuid().ToString('N'))
$source=Join-Path $root 'source';$target=Join-Path $root 'installed';$backup=Join-Path $root 'backup'
New-Item -ItemType Directory -Force -Path $source,$target | Out-Null
Set-Content -LiteralPath (Join-Path $source 'application.txt') -Value 'new'
Set-Content -LiteralPath (Join-Path $target 'application.txt') -Value 'old'
Set-Content -LiteralPath (Join-Path $target 'old-only.txt') -Value 'preserve'
$failed=$false
try {Install-ApplicationPayload -Source $source -Target $target -BackupDirectory $backup -AllowedTargetRoot $root -ValidateInstalled {param($path);throw 'Simulated startup failure'}} catch {$failed=$true}
if(-not $failed -or (Get-Content -LiteralPath (Join-Path $target 'application.txt')) -ne 'old' -or -not (Test-Path -LiteralPath (Join-Path $target 'old-only.txt'))){throw 'Rollback did not restore old application.'}
Install-ApplicationPayload -Source $source -Target $target -BackupDirectory (Join-Path $root 'success-backup') -AllowedTargetRoot $root -ValidateInstalled {param($path);if((Get-Content -LiteralPath (Join-Path $path 'application.txt')) -ne 'new'){throw 'New content missing'}}
if((Test-Path -LiteralPath (Join-Path $target 'old-only.txt')) -or (Get-Content -LiteralPath (Join-Path $target 'application.txt')) -ne 'new'){throw 'Successful install mixed old and new files.'}
[ordered]@{SimulatedFailureRestoredOld=$true;SuccessfulUpgradeClean=$true;Root=$root} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'result.json')
Write-Output "PASS isolated rollback and successful replacement: $root"
