param([string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/Afmc.Find.App/Afmc.Find.App.csproj')
$version = $project.Project.PropertyGroup.Version
if (!$ReleaseDirectory) { $ReleaseDirectory = Join-Path $root ('artifacts/AFMC-FIND-' + $version + '-win-x64') }
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
& (Join-Path $PSScriptRoot 'Verify-StreamingRuntime.ps1') -RuntimeDirectory (Join-Path $release 'components/libtorrent')
if (!(Test-Path -LiteralPath (Join-Path $release 'Streaming/stream_worker.py'))) { throw 'Missing streaming worker' }
$config = Get-Content -Raw -LiteralPath (Join-Path $release 'AFMC.Find.runtimeconfig.json') | ConvertFrom-Json
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$licenses = Join-Path $release 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\HtmlAgilityPack-LICENSE.txt') -Destination $licenses -Force
Copy-Item -LiteralPath (Join-Path $root 'docs\THIRD_PARTY.md') -Destination $licenses -Force
Copy-Item -Path (Join-Path $root 'docs/player-licenses/*') -Destination $licenses -Force
$playerSource = Join-Path $root '.tools/libvlcsharp-3.10.2-source.zip'
if ((Get-FileHash -LiteralPath $playerSource -Algorithm SHA256).Hash -ne '7C7B56D71721E7F3F12607E629AECA9F8416119A24CD4D016CF7F8F1F9940336') { throw 'LibVLCSharp corresponding source archive is missing or mismatched' }
Copy-Item -LiteralPath $playerSource -Destination $licenses -Force
$playwright = Join-Path $release '.playwright'
foreach ($relative in @('package/LICENSE','package/NOTICE','package/ThirdPartyNotices.txt','node/LICENSE','node/win32_x64/node.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $playwright $relative))) { throw "Playwright distribution file missing: $relative" }
}
Copy-Item -LiteralPath (Join-Path $playwright 'package/LICENSE') -Destination (Join-Path $licenses 'Playwright-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $playwright 'package/NOTICE') -Destination (Join-Path $licenses 'Playwright-NOTICE.txt') -Force
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $package = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $directory = Join-Path (Join-Path $packages $package) $framework.version
    $files = Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -like '*NOTICES*' }
    if (-not $files) { throw "License files missing for $package" }
    foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licenses ($framework.name + '-' + $file.Name)) -Force }
}
Copy-Item -LiteralPath (Join-Path $root 'docs\PORTABLE.txt') -Destination (Join-Path $release 'README.txt') -Force
$zip = Join-Path $root ('artifacts/AFMC-FIND-' + $version + '-win-x64.zip')
Compress-Archive -LiteralPath $release -DestinationPath $zip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($required in @('.playwright/node/win32_x64/node.exe','.playwright/package/cli.js','Microsoft.Playwright.dll','Streaming/stream_worker.py','components/libtorrent/python.exe','components/libtorrent/libtorrent-LICENSE.txt','components/libtorrent/LICENSE.txt')) {
        if (!@($archive.Entries | Where-Object { $_.FullName.Replace('\','/').EndsWith($required) }).Count) { throw "Package omitted runtime file: $required" }
    }
} finally { $archive.Dispose() }
$evidence = Join-Path $root ('docs/evidence/v' + $version.Replace('.',''))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $evidence 'package-hash.json')
Get-Item -LiteralPath $zip | Select-Object FullName, Length


