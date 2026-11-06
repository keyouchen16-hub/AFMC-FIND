param([Parameter(Mandatory)][string]$Name, [switch]$Native, [switch]$Retry)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-z0-9-]+$' -or ($Retry -and !$Native)) { throw 'Invalid isolated candidate' }
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'tests/Afmc.Find.Checks/bin/Release/net10.0-windows'
$target = Join-Path $root ".tools/native-retry-bench/$Name"
if (Test-Path -LiteralPath $target) { throw 'Preserve previous experiment' }
New-Item -ItemType Directory -Path $target | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object Extension -in '.dll','.json','.exe' | Copy-Item -Destination $target
Copy-Item -LiteralPath (Join-Path $source 'components') -Destination $target -Recurse
New-Item -ItemType Directory -Path (Join-Path $target 'Streaming') | Out-Null
$worker = [IO.File]::ReadAllText((Join-Path $root 'src/Afmc.Find.App/Streaming/stream_worker.py'))
$injection = ''
if ($Retry) { $injection = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NativePieceRetryCandidate.py')) }
$injection += "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PieceTrace.py'))
$marker = "`ndef main():"
if (!$worker.Contains($marker)) { throw 'Injection marker missing' }
[IO.File]::WriteAllText((Join-Path $target 'Streaming/stream_worker.py'), $worker.Replace($marker,"`n$injection`n$marker"), [Text.UTF8Encoding]::new($false))
if ($Native) {
  $built = @(Get-ChildItem (Join-Path $root '.tools/native-stream-build/build/bindings/python/Release') -Filter 'libtorrent*.pyd')
  if ($built.Count -ne 1) { throw 'Expected one candidate module' }
  Copy-Item -LiteralPath $built[0].FullName -Destination (Join-Path $target 'components/libtorrent/libtorrent/__init__.cp312-win_amd64.pyd') -Force
}
$manifest = @{
  name=$Name; native=[bool]$Native; retry=[bool]$Retry
  workerHash=(Get-FileHash (Join-Path $target 'Streaming/stream_worker.py')).Hash
  nativeHash=(Get-FileHash (Join-Path $target 'components/libtorrent/libtorrent/__init__.cp312-win_amd64.pyd')).Hash
  appHash=(Get-FileHash (Join-Path $target 'AFMC.Find.dll')).Hash
  coreHash=(Get-FileHash (Join-Path $target 'Afmc.Find.Core.dll')).Hash
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $target 'manifest.json') -Encoding utf8
$manifest | ConvertTo-Json
