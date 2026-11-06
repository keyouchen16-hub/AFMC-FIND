param([string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$RuntimeDirectory) { $RuntimeDirectory = Join-Path $root '.tools/libtorrent-runtime' }
$runtimeRoot = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$manifest = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot 'manifest.json') | ConvertFrom-Json
if ($manifest.python -ne '3.12.10' -or $manifest.libtorrent -ne '2.0.15') { throw 'Unexpected streaming runtime version' }
if ($manifest.archives.'python-3.12.10-embed-amd64.zip' -ne '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3' -or
    $manifest.archives.'libtorrent-2.0.15-cp312-cp312-win_amd64.whl' -ne '948c0f5ecd49582c7c9e807f71535e895bb19c648310fa65c8fce1e527a4e514') { throw 'Unexpected streaming archive digest' }
foreach ($entry in $manifest.files.PSObject.Properties) {
    $path = [IO.Path]::GetFullPath((Join-Path $runtimeRoot $entry.Name))
    if (!$path.StartsWith($runtimeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid runtime manifest path' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Value) { throw "Runtime file mismatch: $($entry.Name)" }
}
foreach ($name in @('python.exe','python312.dll','python312.zip','python312._pth','LICENSE.txt','libtorrent-LICENSE.txt','libtorrent/__init__.cp312-win_amd64.pyd')) {
    if (!($manifest.files.PSObject.Properties.Name -contains $name)) { throw "Runtime manifest omitted $name" }
}
$python = Join-Path $runtimeRoot 'python.exe'
$version = & $python -I -c 'import libtorrent as lt; import sys; assert sys.version_info[:3] == (3,12,10); assert lt.version == "2.0.15.0"; print(lt.version)'
if ($LASTEXITCODE -ne 0 -or $version -ne '2.0.15.0') { throw 'Streaming native dependency load failed' }
Write-Output "PASS streaming runtime file digests, licenses, isolated Python and libtorrent $version"
