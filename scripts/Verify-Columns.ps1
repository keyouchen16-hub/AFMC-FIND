$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Query 'Blender'
if($window.Current.Name -ne 'AFMC FIND v0.8.10'){throw 'Wrong installed version'}
if((Find-Control 'SourceStatus').Current.Name -notmatch 'FOSS Torrents · 成功 · [1-9]\d* 条'){throw 'FOSS source did not contribute results'}
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class FindDpi076 { [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle); }
'@
$scale=[FindDpi076]::GetDpiForWindow($process.MainWindowHandle)/96.0
$checks=@()
foreach($pair in @(@('SizeSortHeader','ResultSize'),@('PublishedSortHeader','ResultPublished'),@('SeedsSortHeader','ResultSeeds'))) {
    if($queryControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'Blender'){throw 'User changed the query; stopping layout inspection'}
    $header=Find-Control $pair[0]
    $value=Find-Control $pair[1]
    if($null -eq $value -or $value.Current.IsOffscreen){throw "No visible data cell: $($pair[1])"}
    $headerRight=$header.Current.BoundingRectangle.Right-16*$scale
    $valueRight=$value.Current.BoundingRectangle.Right
    $delta=[Math]::Abs($headerRight-$valueRight)
    $checks += [pscustomobject]@{Column=$pair[0];HeaderRight=$headerRight;ValueRight=$valueRight;DeltaPixels=$delta;DpiScale=$scale}
    if($delta -gt 2){throw "Installed column alignment mismatch: $($pair[0]) $delta"}
}
$checks | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'docs/evidence/v0810/installed-column-alignment.json')
$checks | Format-Table
Write-Output 'PASS installed column alignment and FOSS source contribution'
