param([switch]$FullscreenButton,[switch]$SkipDrag)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class PlayerAcceptance {
 [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint action,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]public static extern bool AttachThreadInput(uint a,uint b,bool link);
 [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr h,int a,out int v,int size);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")]public static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr h,uint cmd);
}
'@
function Id($parent,$name) { $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$name)) }
function Click($item) { if(!$item){throw 'Required control missing'}; $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Wait-For([scriptblock]$predicate,[string]$label,[int]$seconds=45) {
    $until=(Get-Date).AddSeconds($seconds)
    $nextTrace=Get-Date
    while((Get-Date) -lt $until) {
        if(&$predicate){Write-Output "PASS $label";return}
        if($label -eq 'installed magnet exchanges metadata and plays' -and (Get-Date) -ge $nextTrace){
            Write-Output "TRACE magnet status=$($status.Current.Name) time=$($time.Current.Name)"
            $nextTrace=(Get-Date).AddSeconds(5)
        }
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out: $label"
}
function Capture($window,$name) {
    $handle=[IntPtr]$window.Current.NativeWindowHandle
    $material=0
    if([PlayerAcceptance]::DwmGetWindowAttribute($handle,38,[ref]$material,4) -ne 0 -or $material -ne 1){throw 'Player video backing is not opaque'}
    $wasTopmost=([PlayerAcceptance]::GetWindowLong($handle,-20) -band 8) -ne 0
    [void][PlayerAcceptance]::SetWindowPos($handle,[IntPtr](-1),0,0,0,0,0x0043)
    try {
    [void][PlayerAcceptance]::SetForegroundWindow($handle)
    if([PlayerAcceptance]::GetForegroundWindow() -ne $handle) {
        [uint32]$ownerProcess=0
        $foreground=[PlayerAcceptance]::GetWindowThreadProcessId([PlayerAcceptance]::GetForegroundWindow(),[ref]$ownerProcess)
        $current=[PlayerAcceptance]::GetCurrentThreadId()
        $attached=$foreground -ne $current -and [PlayerAcceptance]::AttachThreadInput($current,$foreground,$true)
        try{[void][PlayerAcceptance]::SetForegroundWindow($handle)}finally{if($attached){[void][PlayerAcceptance]::AttachThreadInput($current,$foreground,$false)}}
    }
    Start-Sleep -Milliseconds 350
    $front=[PlayerAcceptance]::GetForegroundWindow()
    $owner=$front
    for($i=0;$i -lt 8 -and $owner -ne [IntPtr]::Zero -and $owner -ne $handle;$i++){$owner=[PlayerAcceptance]::GetWindow($owner,4)}
    if($owner -ne $handle){Write-Warning "Installed screenshot not verified: Windows foreground activation denied (player=$handle foreground=$front)";return}
    $rect=$window.Current.BoundingRectangle
    $bitmap=[Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height);$graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{$graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size);$bitmap.Save((Join-Path $root "docs/evidence/v0817/$name.png"),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
    } finally { if(!$wasTopmost){[void][PlayerAcceptance]::SetWindowPos($handle,[IntPtr](-2),0,0,0,0,0x0003)} }
}
$evidence=Join-Path $root 'docs/evidence/v0817';New-Item -ItemType Directory -Force $evidence | Out-Null
$fixture=Join-Path $root '.tools/player-installed-fixture'
New-Item -ItemType Directory -Force $fixture | Out-Null
$manifest=Join-Path $fixture 'server.json'
if(Test-Path -LiteralPath $manifest){Remove-Item -LiteralPath $manifest}
$server=Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList @('tests/Afmc.Find.Checks/bin/Release/net10.0-windows/Afmc.Find.Checks.dll','--serve-player-fixture') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'fixture-server.log') -RedirectStandardError (Join-Path $evidence 'fixture-server-error.log')
$player=$null
. (Join-Path $PSScriptRoot 'Player-DragAcceptance.ps1')
try {
    Wait-For { Test-Path -LiteralPath $manifest } 'controlled HTTP and BT fixtures ready' 30
    $links=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    $target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
    $process=Get-Process AFMC.Find | Where-Object Path -EQ $target | Select-Object -First 1
    if(!$process){throw 'Installed application is not running'}
    $main=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    if($main.Current.Name -ne 'AFMC FIND v0.8.17'){throw 'Wrong installed version'}
    Click (Id $main 'PlayerEntryButton')
    Wait-For {
        $script:player=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'AFMC FIND · 播放器'),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$process.Id)))
        $null -ne $script:player
    } 'installed player entry opens native window' 10
    Wait-For { $logo=Id $script:player 'PlayerIdleLogo';$logo -and !$logo.Current.IsOffscreen } 'installed idle player displays software logo' 5
    if(Id $player 'PlayerBrand'){throw 'Old brand caption still visible'}
    Capture $player 'installed-player-idle-logo'
    $idleOverlay=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'LibVLCSharp.WPF'))
    if(!$SkipDrag){Test-InstalledPlayerDrag $player $idleOverlay $false}
    if (!(Id $player 'AddressBox')) { Click (Id $player 'SourceToggle') }
    $address=Id $player 'AddressBox';$status=Id $player 'PlayerStatus';$time=Id $player 'TimeLabel'
    $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($links.Direct)
    Click (Id $player 'LoadButton')
    Wait-For { $status.Current.Name -like '正在播放*' -and $time.Current.Name -notlike '00:00*' } 'installed direct video plays and time advances'
    $logo=Id $player 'PlayerIdleLogo';if($logo -and !$logo.Current.IsOffscreen){throw 'Idle logo overlays video'}
    $overlay=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'LibVLCSharp.WPF'))
    if(!$overlay){throw 'Video overlay not found'}
    if(!$SkipDrag){Test-InstalledPlayerDrag $player $overlay $true}
    $overlayHandle=[IntPtr]$overlay.Current.NativeWindowHandle
    $before=$player.Current.BoundingRectangle
    function DoubleVideo {
        $bounds=$overlay.Current.BoundingRectangle
        $point=[Drawing.Point]::new([int]($bounds.Left+$bounds.Width/2),[int]($bounds.Top+$bounds.Height/2))
        if([PlayerAcceptance]::WindowFromPoint($point) -ne $overlayHandle){throw "Video overlay not uppermost at test point"}
        [void][PlayerAcceptance]::SetCursorPos($point.X,$point.Y)
        Start-Sleep -Milliseconds 100
        foreach($action in 2,4,2,4){[PlayerAcceptance]::mouse_event($action,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 45}
    }
    $hw=[IntPtr]$player.Current.NativeWindowHandle
    [void][PlayerAcceptance]::SetWindowPos($hw,[IntPtr](-1),0,0,0,0,0x43)
    try {
        Start-Sleep -Milliseconds 300
        if($FullscreenButton){Click (Id $player 'FullscreenButton')}else{DoubleVideo}
        Start-Sleep -Milliseconds 350
        Write-Output ("Double click rect {0} => {1}, button={2}" -f $before,$player.Current.BoundingRectangle,(Id $player 'FullscreenButton').Current.Name)
        Wait-For { $player.Current.BoundingRectangle.Height -gt ($before.Height + 20) } "video fullscreen enters (button=$FullscreenButton)" 3
        Start-Sleep -Milliseconds 650
        if($FullscreenButton){Click (Id $player 'FullscreenButton')}else{DoubleVideo}
        Wait-For { [Math]::Abs($player.Current.BoundingRectangle.Height-$before.Height) -lt 4 } "video fullscreen restores (button=$FullscreenButton)" 3
    } finally {[void][PlayerAcceptance]::SetWindowPos($hw,[IntPtr](-2),0,0,0,0,0x03)}
    Capture $player 'installed-player-direct'
    Click (Id $player 'PauseButton')
    Wait-For { $status.Current.Name -like '已暂停*' } 'installed pause button works' 5
    Click (Id $player 'PauseButton')
    Click (Id $player 'StopButton')
    Wait-For { $status.Current.Name -like '已停止*' } 'installed stop completes' 10
    Wait-For { $logo=Id $player 'PlayerIdleLogo';$logo -and !$logo.Current.IsOffscreen } 'stop restores the idle logo' 5
    if (!(Id $player 'LoadButton')) {
        Click (Id $player 'SourceToggle')
        Wait-For { $null -ne (Id $player 'LoadButton') } 'open resource reveals the address field' 5
    }
    $address = Id $player 'AddressBox'
    $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($links.Magnet)
    if($address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $links.Magnet){throw 'Fixture magnet input changed before submit'}
    $playbackTimer=[Diagnostics.Stopwatch]::StartNew()
    Click (Id $player 'LoadButton')
    Wait-For { $status.Current.Name -like '正在播放*' -and $time.Current.Name -notlike '00:00*' } 'installed magnet exchanges metadata and plays' 100
    $progress=$status.Current.Name
    if($progress -match '下载 100[.,]0%'){throw 'Magnet fixture completed before playback observation'}
    $observedSeconds=[Math]::Round($playbackTimer.Elapsed.TotalSeconds,2)
    Capture $player 'installed-player-magnet'
    $player.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Wait-For { @(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'AFMC/Find/PlaybackCache') -Directory -ErrorAction SilentlyContinue).Count -eq 0 } 'installed close removes playback cache' 12
    [pscustomobject]@{Version='0.8.17';Process=$process.Id;Direct=$true;Magnet=$true;ProgressAtPlayback=$progress;SecondsUntilObservedClock=$observedSeconds;Pause=$true;CloseCleanup=$true;Backdrop=1;Input='UI Automation';Media='Original generated AVI colour bars and PCM audio; controlled loopback peers'} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $evidence 'installed-player.json')
} finally {
    if($player){try{$player.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}catch{}}
    Set-Content -LiteralPath (Join-Path $fixture 'stop') -Value 'stop'
    if(!$server.WaitForExit(10000)){Stop-Process -InputObject $server -Force}
}
