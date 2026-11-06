param([ValidatePattern('^[a-z0-9-]+$')][string]$EvidenceName='installed-movie')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$ui=[System.Windows.Automation.AutomationElement]
$scope=[System.Windows.Automation.TreeScope]::Descendants
$root=Split-Path $PSScriptRoot -Parent
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
$process=Get-Process AFMC.Find | Where-Object Path -eq $target | Select-Object -First 1
$main=$ui::FromHandle($process.MainWindowHandle)
function ById($parent,$id){$parent.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty,$id))}
function ClockSeconds($text){
    $parts=($text.Split('/')[0].Trim()).Split(':')
    if($parts.Count -eq 2){return [int]$parts[0]*60+[int]$parts[1]}
    return [int]$parts[0]*3600+[int]$parts[1]*60+[int]$parts[2]
}
$rows=(ById $main 'ResultsList').FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
if(!$rows.Count){throw 'No results'}
$texts=$rows[0].FindAll($scope,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
$names=@($texts | ForEach-Object {$_.Current.Name})
if(($names -join ' ') -notmatch '已验证可播'){throw 'First result has no verified playback evidence'}
$report=[ordered]@{Version=$main.Current.Name;Title=$names[0];FirstRankVerified=$true;Completed=$false;Samples=@()}
$player=$null
$watch=[Diagnostics.Stopwatch]::StartNew()
try {
    $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (ById $main 'PlayButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    do {
        Start-Sleep -Milliseconds 100
        $player=$main.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($ui::NameProperty,'AFMC FIND · 播放器'))
    } while(!$player -and $watch.Elapsed.TotalSeconds -lt 10)
    if(!$player){throw 'Player did not open'}
    (ById $player 'VolumeBar').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(0)
    $status=ById $player 'PlayerStatus';$clock=ById $player 'TimeLabel'
    do {
        Start-Sleep -Seconds 2
        $phase=$status.Current.Name
        Write-Output $phase
        $seconds=ClockSeconds $clock.Current.Name
    } while(($phase -notlike '正在播放*' -or $seconds -lt 2) -and $watch.Elapsed.TotalSeconds -lt 100)
    if($phase -notlike '正在播放*' -or $seconds -lt 2){throw 'Verified search result did not start in installed player'}
    $report.StartSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,2)
    $initial=$seconds
    foreach($index in 1..9){
        Start-Sleep -Seconds 5
        $report.Samples+=@{At=[Math]::Round($watch.Elapsed.TotalSeconds,2);Clock=$clock.Current.Name;Status=$status.Current.Name}
        Write-Output ($report.Samples[-1] | ConvertTo-Json -Compress)
    }
    $advanced=(ClockSeconds $clock.Current.Name)-$initial
    $report.PlaybackAdvanceSeconds=$advanced
    if($advanced -lt 40){throw 'Installed movie playback stalled during 45-second observation'}
    $report.Completed=$true
    Write-Output 'PASS installed first verified movie starts and clock advances at least 40 seconds during 45 seconds'
} catch {$report.Error=$_.Exception.Message;throw}
finally {
    if($player){try{$player.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}catch{}}
    $report | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $root "docs/evidence/v0812/$EvidenceName.json")
}
