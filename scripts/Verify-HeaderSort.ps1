$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Query 'ubuntu'
if($window.Current.Name -ne 'AFMC FIND v0.8.10'){throw 'Wrong installed version'}
if((Find-Control 'SourceStatus').Current.Name -notmatch '海盗湾 · 成功 · [1-9]\d* 条'){throw 'Direct Pirate Bay source did not contribute results'}
$checks=@()
foreach($id in 'SizeSortHeader','PublishedSortHeader','SeedsSortHeader') {
    if($queryControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'ubuntu'){throw 'User changed the query; stopping UI verification to avoid interference'}
    $control=Find-Control $id
    if($null -eq $control -or $control.Current.IsOffscreen -or -not $control.Current.IsEnabled){throw "Header not available: $id"}
    foreach($arrow in '↓','↑') {
        $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 150
        $label=$control.Current.Name
        $sortLabel=(Find-Control 'SortButton').Current.Name
        if(-not $label.EndsWith($arrow) -or -not $sortLabel.EndsWith($arrow)){throw "Header or toolbar direction mismatch: $id $label $sortLabel"}
        $checks += [pscustomobject]@{Header=$id;Label=$label;Toolbar=$sortLabel;Width=$control.Current.BoundingRectangle.Width;Method='UIA InvokePattern'}
    }
}
$checks | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'docs/evidence/v0810/installed-header-sort.json')
if($queryControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'ubuntu'){Write-Output 'Header actions recorded; screenshot skipped because the user changed the query';return}
$rect=New-Object FindCapture+Rect
[FindCapture]::GetWindowRect($process.MainWindowHandle,[ref]$rect) | Out-Null
$image=New-Object System.Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
$graphics=[System.Drawing.Graphics]::FromImage($image)
try {
    $dc=$graphics.GetHdc()
    try {if(-not [FindCapture]::PrintWindow($process.MainWindowHandle,$dc,2)){throw 'Capture failed'}}finally{$graphics.ReleaseHdc($dc)}
    $image.Save((Join-Path $root 'docs/evidence/v0810/installed-header-sort.png'),[System.Drawing.Imaging.ImageFormat]::Png)
} finally {$graphics.Dispose();$image.Dispose()}
$checks | Format-Table
Write-Output 'PASS installed Pirate Bay results and six header actions'
