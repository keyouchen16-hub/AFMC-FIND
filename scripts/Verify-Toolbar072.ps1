$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Query '特洛伊' -CheckImages
$p=Get-Process AFMC.Find | Where-Object Path -eq (Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe') | Select-Object -First 1
$w=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function ById($id){$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))}
function Pick($control,$label){
 $control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 Start-Sleep -Milliseconds 200
 $condition=[System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id),[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$label),[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
 $item=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object { $_.GetSupportedPatterns().Id -contains [System.Windows.Automation.SelectionItemPattern]::Pattern.Id } | Select-Object -First 1
 if(!$item){throw "Dropdown option not exposed: $label"}
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 $control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
 Start-Sleep -Milliseconds 200
}
$category=ById 'CategoryBox';$resolution=ById 'ResolutionBox';$filter=ById 'FilterOptionsButton'
if(!$category -or !$resolution -or !$filter){throw 'Toolbar controls missing'}
foreach($id in 'HistoryButton','FavoritesButton','HealthButton'){if(ById $id){throw "Removed entry exposed: $id"}}
Pick $category '视频';if(!$resolution.Current.IsEnabled){throw 'Video resolution disabled'}
Pick $resolution '1080p'
$count=(ById 'CountText').Current.Name
if($count -match '^0 /'){throw 'No video 1080p result'}
$bounds=$category.Current.BoundingRectangle;$rb=$resolution.Current.BoundingRectangle;$fb=$filter.Current.BoundingRectangle
if($rb.Left -lt $bounds.Right -or $fb.Left -lt $rb.Right -or [Math]::Abs($bounds.Top-$rb.Top) -gt 3){throw 'Toolbar overlap or alignment failure'}
Pick $category '音乐';if($resolution.Current.IsEnabled){throw 'Music incorrectly enables resolution'}
Pick $category '视频'
[FindCapture]::ShowWindow($p.MainWindowHandle,9)|Out-Null
[FindCapture]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
Start-Sleep -Milliseconds 300
$rect=New-Object FindCapture+Rect;[FindCapture]::GetWindowRect($p.MainWindowHandle,[ref]$rect)|Out-Null
$bitmap=New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top));$g=[Drawing.Graphics]::FromImage($bitmap)
try{$dc=$g.GetHdc();try{if(![FindCapture]::PrintWindow($p.MainWindowHandle,$dc,2)){throw 'Capture failed'}}finally{$g.ReleaseHdc($dc)};$bitmap.Save((Join-Path $root 'docs/evidence/v072/toolbar-installed.png'))}finally{$g.Dispose();$bitmap.Dispose()}
@{Version=(Get-Item (Join-Path (Split-Path $p.Path) 'AFMC.Find.dll')).VersionInfo.ProductVersion;Video1080pCount=$count;RemovedEntriesAbsent=$true;DropdownsSelectedViaUIA=$true;MusicDisablesResolution=$true;Aligned=$true;Method='Installed WPF UIA ExpandCollapse + SelectionItem, PrintWindow'} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root 'docs/evidence/v072/toolbar-installed.json')
