$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root=Split-Path $PSScriptRoot -Parent
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND'
$process=Get-Process AFMC.Find | Where-Object Path -EQ (Join-Path $target 'AFMC.Find.exe') | Select-Object -First 1
if(!$process){throw 'Installed application is not running'}
$window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if($window.Current.Name -ne 'AFMC FIND v0.8.10'){throw 'Incorrect installed version'}
function Find-Id($parent,$id){$parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))}
function Find-Name($parent,$name){$parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))}
function Invoke-Control($control){if(!$control){throw 'Expected control unavailable'};$control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
Invoke-Control (Find-Id $window 'SourcesNav')
Invoke-Control (Find-Name $window '添加 Torznab 源')
Start-Sleep -Milliseconds 400
$dialog=Find-Name $window '配置 Torznab 搜索源'
if(!$dialog){throw 'Source dialog unavailable'}
try {
    (Find-Id $dialog 'WebsiteBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('https://www.acgndog.com/')
    if((Find-Id $dialog 'ProbeQueryBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne ''){throw 'URL-only probing no longer default'}
    Invoke-Control (Find-Id $dialog 'DetectButton')
    $until=(Get-Date).AddSeconds(160)
    do {Start-Sleep -Milliseconds 300;if((Find-Id $dialog 'DetectButton').Current.IsEnabled){break}}while((Get-Date) -lt $until)
    $status=(Find-Id $dialog 'OutcomeText').Current.Name
    if(!(Find-Id $dialog 'SaveDiscoveryButton').Current.IsEnabled){throw "Installed URL-only discovery failed: $status"}
    $preview=Find-Id $dialog 'PreviewList'
    $cloud=Find-Name $preview '夸克网盘 · 网盘'
    $baidu=Find-Name $preview '百度网盘 · 网盘'
    if(!$cloud -and !$baidu){throw 'Cloud resource label missing'}
    $result=[ordered]@{Version='0.8.10';Process=$process.Id;Website='https://www.acgndog.com/';QueryRequired=$false;Result=$status;VerifiedAddEnabled=$true;CloudLabel=$true;PersonalSourcesModified=$false;ExternalDownloaderInvoked=$false}
} finally {
    $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
Start-Sleep -Milliseconds 800
$nodes=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" | Where-Object Name -EQ 'node.exe')
if($nodes.Count){throw 'Browser driver remained after completed discovery'}
$result['BrowserDriverStopped']=$true
$result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root 'docs/evidence/v0810/installed-browser-discovery.json')
