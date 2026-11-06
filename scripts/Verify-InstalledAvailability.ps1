param([string]$Query='蜘蛛侠',[ValidatePattern('^[a-z0-9-]+$')][string]$EvidenceName='installed-search')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$ui=[System.Windows.Automation.AutomationElement]
$scope=[System.Windows.Automation.TreeScope]::Descendants
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
$process=Get-Process AFMC.Find | Where-Object Path -eq $target | Select-Object -First 1
$main=$ui::FromHandle($process.MainWindowHandle)
$reportPath=Join-Path $PSScriptRoot "../docs/evidence/v0812/$EvidenceName.json"
@{Status='Running';Process=$process.Id;Started=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -Encoding UTF8 $reportPath
function ById($id) { $main.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty,$id)) }
if($main.Current.Name -ne 'AFMC FIND v0.8.12'){throw 'Wrong installed version'}
$queryBox=ById 'QueryBox'
$queryBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Query)
(ById 'SearchButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$until=(Get-Date).AddMinutes(5)
$previous=''
$seenProbe=$false
do {
    Start-Sleep -Seconds 2
    $process.Refresh()
    if($process.HasExited){@{Status='Failed';Reason='Application exited during search/probe';Process=$process.Id} | ConvertTo-Json | Set-Content -Encoding UTF8 $reportPath;throw 'Application exited during search/probe'}
    $availability=(ById 'AvailabilityText').Current.Name
    $count=(ById 'CountText').Current.Name
    $searching=(ById 'SearchButton').Current.Name -ne '搜索资源'
    $state="$count | $availability | searching=$searching"
    if($state -ne $previous){Write-Output $state;$previous=$state}
    if($availability -like '*检测中*'){$seenProbe=$true}
    $done=($seenProbe -and !$searching -and $availability -and $availability -notlike '*检测中*')
} while(!$done -and (Get-Date) -lt $until)
if(!$done){throw 'Installed search/probe did not finish within five minutes'}
$rows=(ById 'ResultsList').FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
$samples=@()
for($index=0;$index -lt [Math]::Min(12,$rows.Count);$index++) {
    $texts=$rows[$index].FindAll($scope,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
    $names=@($texts | ForEach-Object {$_.Current.Name})
    if($names.Count){$samples += [pscustomobject]@{Index=$index;Text=$names}}
}
$report=[ordered]@{Version=$main.Current.Name;Query=$Query;Count=$count;Availability=$availability;VisibleRows=$samples;ObservedPending=$seenProbe;Completed=$done}
$report | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $reportPath
if($availability -notmatch '暂无已验证可播资源|已验证可播 \d+ 条'){throw 'Availability summary missing'}
if(!$samples.Count){throw 'No result rows exposed for installed verification'}
if(($samples[0].Text -join ' ') -match '本次连接超时|本次试播失败'){throw 'Failed resource still ranked first'}
Write-Output 'PASS installed search completes, availability is explicit and first visible row is not a known failed probe'
