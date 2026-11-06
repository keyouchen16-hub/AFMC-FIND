$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$helper=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Verify-Player.ps1'))
Invoke-Expression ($helper.Substring(0,$helper.IndexOf('$evidence=')).Replace('$root = Split-Path $PSScriptRoot -Parent',''))
$process=Get-Process AFMC.Find | Where-Object Path -eq (Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe') | Select-Object -First 1
$main=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Click (Id $main 'PlayerEntryButton')
Start-Sleep -Milliseconds 350
$player=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'AFMC FIND · 播放器'))
try {
    $overlay=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'LibVLCSharp.WPF'))
    $rect=$overlay.Current.BoundingRectangle
    foreach($name in 'SourceToggle','PauseButton','SeekBar','SubtitleButton'){
        $item=Id $overlay $name
        if(!$item -or !$rect.Contains($item.Current.BoundingRectangle)){throw "Idle $name is outside the video overlay"}
    }
    if(Id $player 'AddressBox'){throw 'Idle address bar unexpectedly occupies top row'}
    if(!(Id $player 'SourceToggle').Current.IsEnabled){throw 'Open resource entry unavailable'}
    Write-Output 'PASS installed idle controls contained inside visible floating bar'
    $hwnd=[IntPtr]$player.Current.NativeWindowHandle
    $normal=$player.Current.BoundingRectangle
    [void][PlayerAcceptance]::PostMessage($hwnd,0x112,[IntPtr]0xF030,[IntPtr]::Zero)
    Wait-For {
        $video=$overlay.Current.BoundingRectangle
        $frame=$player.Current.BoundingRectangle
        [Math]::Abs($video.Top-$frame.Top) -lt 2 -and [Math]::Abs($video.Height-$frame.Height) -lt 2 -and [Math]::Abs($video.Width-$frame.Width) -lt 2
    } 'maximize entry removes caption and video covers window' 5
    $caption=$player.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'关闭窗口'))
    if($caption -and !$caption.Current.IsOffscreen){throw 'Caption is still visible in fullscreen'}
    Add-Type -AssemblyName System.Windows.Forms
    $screen=[System.Windows.Forms.Screen]::FromHandle($hwnd).Bounds
    $full=$player.Current.BoundingRectangle
    if([Math]::Abs($full.Width-$screen.Width) -gt 2 -or [Math]::Abs($full.Height-$screen.Height) -gt 2){throw 'Window does not cover monitor'}
    Write-Output "PASS idle fullscreen video $full matches monitor $screen and hides caption"
    Capture $player 'installed-idle-fullscreen'
    Click (Id $player 'FullscreenButton')
    Wait-For { [Math]::Abs($player.Current.BoundingRectangle.Height-$normal.Height) -lt 3 } 'fullscreen restores previous window size' 5
} finally { $player.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
