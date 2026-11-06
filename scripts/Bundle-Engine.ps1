param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$archive=Join-Path $root '.tools/jackett/runtime-verified.zip'
$source=Join-Path $root '.tools/jackett/source-verified.zip'
if(!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne '2ACC0F068629125F2577CC2144E0A47A26B4BD8296A42378A7148A91CA97C968'){throw 'Pinned Jackett 0.24.3009 archive missing or hash mismatch; see docs/ENGINE_DEPENDENCY.md'}
if(!(Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source).Hash -ne 'D10B224660EABD571EB421A92AEDF508715811FD0FB8DE38B6B4C70061A7112E'){throw 'Matching Jackett source archive missing or hash mismatch'}
$release=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
$components=Join-Path $release 'components'
New-Item -ItemType Directory -Force -Path $components | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $components -Force
$licenses=Join-Path $release 'licenses/Jackett'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Copy-Item -LiteralPath (Join-Path $components 'Jackett/LICENSE') -Destination $licenses -Force
Copy-Item -LiteralPath $source -Destination (Join-Path $licenses 'Jackett-source-v0.24.3009.zip') -Force
Copy-Item -LiteralPath (Join-Path $root 'docs/ENGINE_DEPENDENCY.md') -Destination $licenses -Force
Write-Output 'PASS bundled pinned Jackett runtime, license, and matching source archive'
