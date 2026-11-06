param([string]$Query="ubuntu", [switch]$CheckImages, [string]$Filter="")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FindCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle,IntPtr dc,uint flags);
}
'@
[FindCapture]::SetProcessDPIAware() | Out-Null
$exe = Join-Path $env:LOCALAPPDATA 'Programs\AFMC FIND\AFMC.Find.exe'
$process = Get-Process -Name 'AFMC.Find' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
if ($null -eq $process) { $process = Start-Process -FilePath $exe -PassThru }
if (-not $process.WaitForInputIdle(15000)) { throw 'Release window did not become ready' }
$process.Refresh()
$readyDeadline = [DateTime]::UtcNow.AddSeconds(15)
while ($process.MainWindowHandle -eq [IntPtr]::Zero -and -not $process.HasExited -and [DateTime]::UtcNow -lt $readyDeadline) {
    Start-Sleep -Milliseconds 150
    $process.Refresh()
}
if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Release has no main window' }
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if ($null -eq $window) { throw 'Release window unavailable' }
function Find-Control([string]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
$queryControl = Find-Control 'QueryBox'
$search = Find-Control 'SearchButton'
if ($null -eq $queryControl -or $null -eq $search) { throw 'Search controls unavailable' }
$queryControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Query)
$search.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$deadline = [DateTime]::UtcNow.AddSeconds(100)
do {
    Start-Sleep -Milliseconds 250
    if($queryControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $Query){throw 'User changed the query; stopping verification to avoid interfering with their work'}
    $status = (Find-Control 'StatusText').Current.Name
} until ($status -like '*搜索完成*' -or [DateTime]::UtcNow -gt $deadline)
if ($status -notlike '*搜索完成*' -or $status -like '*· 0 条结果*') { throw 'Published release did not return search results' }
if ($null -ne (Find-Control 'ImagesOnly')) { throw 'Obsolete image filter is still visible' }
$thumbnailCount = 0
if ($Filter) {
    $filterCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '筛选当前结果')
    $filterBox = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $filterCondition)
    if ($null -eq $filterBox) { throw 'Text filter unavailable' }
    $filterBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Filter)
}
if ($CheckImages) {
    $imageDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $imageCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '来源缩略图已加载')
        $thumbnailCount = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $imageCondition).Count
    } until ($thumbnailCount -gt 0 -or [DateTime]::UtcNow -gt $imageDeadline)
    if ($thumbnailCount -lt 1) { throw 'No thumbnail rendered in release window' }
}
[FindCapture]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
[FindCapture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300
$rect = New-Object FindCapture+Rect
[FindCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
$image = New-Object System.Drawing.Bitmap(($rect.Right-$rect.Left), ($rect.Bottom-$rect.Top))
$graphics = [System.Drawing.Graphics]::FromImage($image)
try {
    $dc=$graphics.GetHdc(); try { if(-not [FindCapture]::PrintWindow($process.MainWindowHandle,$dc,2)){throw "Window capture failed"} } finally {$graphics.ReleaseHdc($dc)}
    $image.Save((Join-Path $root 'docs\evidence\v0810\published-window.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $image.Dispose() }
$result = [ordered]@{
    version = (Get-Item (Join-Path (Split-Path $exe -Parent) 'AFMC.Find.dll')).VersionInfo.ProductVersion
    query = $Query
    textFilter = $Filter
    thumbnailCount = $thumbnailCount
    executable = $exe
    pid = $process.Id
    status = $status
    sourceStatus = (Find-Control 'SourceStatus').Current.Name
    responding = $process.Responding
    queryMethod = 'Windows UI Automation ValuePattern and InvokePattern'
    sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    externalDownloaderInvoked = $false
}
$result | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'docs\evidence\v0810\published-verification.json')
$result | ConvertTo-Json


