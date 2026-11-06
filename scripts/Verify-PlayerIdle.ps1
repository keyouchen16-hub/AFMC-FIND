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
    Capture $player 'installed-idle'
    Click (Id $player 'SourceToggle')
    if(!(Id $player 'AddressBox')){throw 'Cannot open address entry'}
    Write-Output 'PASS idle open resource reveals working address entry'
} finally { $player.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
