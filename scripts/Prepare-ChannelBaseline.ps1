$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$installed = Join-Path $env:LOCALAPPDATA 'Programs\AFMC FIND'
$checks = Join-Path $root 'tests\Afmc.Find.Checks\bin\Release\net10.0-windows'
$target = Join-Path $root '.tools\channel-bench\baseline-0820'
if (Test-Path -LiteralPath $target) { throw 'Baseline already exists; preserve the previous evidence.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installed 'AFMC.Find.dll')).ProductVersion
if ($version -notmatch '^0\.8\.20(?:\+|$)') { throw "Installed version changed: $version" }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $checks '*') -Destination $target -Recurse
$manifest = foreach ($relative in @('AFMC.Find.dll', 'Afmc.Find.Core.dll', 'Streaming\stream_worker.py')) {
    $source = Join-Path $installed $relative
    $destination = Join-Path $target $relative
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $originalHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $copyHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($originalHash -ne $copyHash) { throw "Baseline hash mismatch: $relative" }
    [pscustomobject]@{ File = $relative; InstalledHash = $originalHash; CopiedHash = $copyHash; Version = $version }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'baseline-manifest.json') -Encoding utf8
$manifest | Format-Table -AutoSize
