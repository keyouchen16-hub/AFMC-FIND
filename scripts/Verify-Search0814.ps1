param([string]$Query='少林足球', [string]$Version='0.8.14', [string]$EvidenceName='v0814')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root "docs/evidence/$EvidenceName"
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND'
$process=Get-Process AFMC.Find | Where-Object Path -eq (Join-Path $target 'AFMC.Find.exe') | Select-Object -First 1
$ui=[System.Windows.Automation.AutomationElement];$scope=[System.Windows.Automation.TreeScope]::Descendants
$window=$ui::FromHandle($process.MainWindowHandle)
function Id($name){$window.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty,$name))}
function Click($item){$item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
if($window.Current.Name -ne "AFMC FIND v$Version"){throw "Expected installed $Version"}
$hashes=foreach($name in 'AFMC.Find.dll','Afmc.Find.Core.dll','Streaming/stream_worker.py'){
 $candidate=(Get-FileHash -LiteralPath (Join-Path $root "artifacts/AFMC-FIND-$Version-win-x64/$name")).Hash
 $installed=(Get-FileHash -LiteralPath (Join-Path $target $name)).Hash
 if($candidate -ne $installed){throw "Installed mismatch: $name"}
 [pscustomobject]@{Name=$name;Hash=$installed;Matches=$true}
}
$baseline=Get-Content (Join-Path $folder 'profile-before.json') -Raw | ConvertFrom-Json
$profile=Join-Path $env:LOCALAPPDATA 'AFMC/Find'
foreach($entry in $baseline){if((Get-FileHash -LiteralPath (Join-Path $profile $entry.Name)).Hash -ne $entry.Hash){throw "Profile changed: $($entry.Name)"}}
(Id 'SearchButton').SetFocus()
Start-Sleep -Milliseconds 250
(Id 'QueryBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Query)
$submitted=(Id 'QueryBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
if($submitted -ne $Query){throw 'Input changed before submission; search test not started'}
for($i=0;$i -lt 6 -and (Id 'SortButton').Current.Name -notlike '*综合推荐*';$i++){Click (Id 'SortButton')}
$clock=[Diagnostics.Stopwatch]::StartNew();Click (Id 'SearchButton');$first=$null;$timeline=@();$last=''
do{
 Start-Sleep -Milliseconds 300
 $process.Refresh();if($process.HasExited){throw 'Application exited during search'}
 $count=(Id 'CountText').Current.Name;$status=(Id 'StatusText').Current.Name
 $list=Id 'ResultsList';$items=$list.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
 if($items.Count -gt 0 -and $null -eq $first){$first=$clock.ElapsedMilliseconds}
 if($status -ne $last){$timeline+=@{Ms=$clock.ElapsedMilliseconds;Status=$status;Count=$count};$last=$status}
 $done=(Id 'SearchButton').Current.Name -eq '搜索资源'
}while(!$done -and $clock.Elapsed.TotalSeconds -lt 160)
if(!$done){Click (Id 'CancelButton');throw 'Installed search exceeded acceptance deadline'}
$titles=@();for($i=0;$i -lt [Math]::Min(12,$items.Count);$i++){
 $texts=$items[$i].FindAll($scope,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
 $titles+=,@($texts | ForEach-Object {$_.Current.Name})
}
$report=[ordered]@{Version=$window.Current.Name;Query=$Query;Submitted=$submitted;InputAtCompletion=(Id 'QueryBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value;FirstResultMs=$first;SearchCompletedMs=$clock.ElapsedMilliseconds;Count=$count;Timeline=$timeline;FirstRows=$titles;CandidateHashes=$hashes;ProfileHashesPreserved=$true;PlaybackVerified=$false}
$report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $folder 'installed-search.json')
if(!$titles.Count){throw 'No matching results'}
if($Query -eq '少林足球' -and @($titles | Where-Object {($_ -join ' ') -notmatch '少林足球|(?i)shaolin|siu.?lam'}).Count){throw 'Unrelated title appeared in first installed results'}
Write-Output "PASS installed $($window.Current.Name): $count; first=$first ms; complete=$($clock.ElapsedMilliseconds) ms; first $($titles.Count) rows match query; profile and candidate hashes preserved"
